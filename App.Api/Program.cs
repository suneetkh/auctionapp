using System.Text;
using App.Api.Data;
using App.Api.Hubs;
using App.Api.Models;
using App.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "app.db");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddControllers().AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    opts.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddScoped<TeamLedgerService>();
builder.Services.AddScoped<BidValidationService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<SaleService>();
builder.Services.AddScoped<WheelSelectionService>();
builder.Services.AddScoped<UnsoldPoolService>();
builder.Services.AddScoped<CorrectionService>();
builder.Services.AddScoped<CaptainAssignmentService>();
builder.Services.AddScoped<AuctionLifecycleService>();
builder.Services.AddScoped<AuctionDeletionService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<IAuctionBroadcaster, AuctionBroadcaster>();

const string developmentJwtKey = "dev-super-secret-key-please-change-32chars-minimum!";
var jwtKey = builder.Configuration["Jwt:Key"];
if (builder.Environment.IsProduction() &&
    (string.IsNullOrWhiteSpace(jwtKey) || jwtKey == developmentJwtKey || jwtKey.Length < 32))
{
    throw new InvalidOperationException(
        "Production requires Jwt__Key to be set to a unique value of at least 32 characters.");
}
jwtKey ??= developmentJwtKey;
builder.Configuration["Jwt:Key"] = jwtKey;
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "AuctionApp";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "AuctionAppClient";
builder.Configuration["Jwt:Issuer"] = jwtIssuer;
builder.Configuration["Jwt:Audience"] = jwtAudience;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };

    // Allow SignalR to receive the JWT via query string (access_token)
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    if (app.Environment.IsDevelopment())
    {
        await SeedData.SeedAsync(db);
    }

    // Production bootstrap: creates the very first SuperAdmin from env vars, but only when
    // the Users table is completely empty. This is how you get your own real admin login
    // without ever hardcoding a demo password - set BOOTSTRAP_ADMIN_EMAIL and
    // BOOTSTRAP_ADMIN_PASSWORD once on the host, restart, then you can remove them (the
    // check below makes it a no-op forever after the first user exists, so leaving them set
    // is harmless too, but removing keeps the password out of your host's env var history).
    if (!await db.Users.AnyAsync())
    {
        var bootstrapEmail = builder.Configuration["BOOTSTRAP_ADMIN_EMAIL"];
        var bootstrapPassword = builder.Configuration["BOOTSTRAP_ADMIN_PASSWORD"];
        if (!string.IsNullOrWhiteSpace(bootstrapEmail) && !string.IsNullOrWhiteSpace(bootstrapPassword))
        {
            db.Users.Add(new User
            {
                Email = bootstrapEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(bootstrapPassword),
                Role = UserRole.SuperAdmin,
                DisplayName = "Admin"
            });
            await db.SaveChangesAsync();
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseDefaultFiles();
app.UseResponseCompression();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AuctionHub>("/hubs/auction");

app.MapFallbackToFile("/index.html");

app.Run();
