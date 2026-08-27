using App.Api.Data;
using App.Api.DTOs;
using App.Api.Models;
using App.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace App.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;

    public AuthController(AppDbContext db, JwtService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        var identifier = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == identifier);
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid email or password" });

        var token = _jwt.GenerateToken(user);
        return Ok(new LoginResponse(token, user.Email, user.Role.ToString(), user.DisplayName, user.Id));
    }

    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public record ChangeIdentifierRequest(string CurrentPassword, string NewIdentifier);

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();

        var user = await _db.Users.FindAsync(id);
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return Unauthorized(new { error = "Current password is incorrect" });

        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 12 ||
            !req.NewPassword.Any(char.IsUpper) || !req.NewPassword.Any(char.IsLower) ||
            !req.NewPassword.Any(char.IsDigit) || !req.NewPassword.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            return BadRequest(new
            {
                error = "New password must be at least 12 characters and include uppercase, lowercase, a number, and a symbol"
            });
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Password changed successfully. Sign in again with the new password." });
    }

    [HttpPost("change-identifier")]
    [Authorize]
    public async Task<IActionResult> ChangeIdentifier(ChangeIdentifierRequest req)
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();
        var user = await _db.Users.FindAsync(id);
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return Unauthorized(new { error = "Current password is incorrect" });

        var identifier = req.NewIdentifier.Trim().ToLowerInvariant();
        if (identifier.Length < 3 || identifier.Any(char.IsWhiteSpace))
            return BadRequest(new { error = "Username or email must be at least 3 characters and cannot contain spaces" });
        if (await _db.Users.AnyAsync(u => u.Id != id && u.Email == identifier))
            return Conflict(new { error = "That username or email is already in use" });

        user.Email = identifier;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Login name changed. Sign in again with the new username or email." });
    }

    [HttpPost("logout")]
    public IActionResult Logout() => Ok(new { message = "Logged out (discard token client-side)" });

    [HttpGet("/api/me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();
        var user = await _db.Users.FindAsync(id);
        if (user == null) return Unauthorized();
        return Ok(new { user.Id, user.Email, Role = user.Role.ToString(), user.DisplayName });
    }
}
