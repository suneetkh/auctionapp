using App.Api.Data;
using App.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

public record CreateUserRequest(string Email, string Password, string DisplayName, int[] AuctionIds);
public record UpdateUserAccessRequest(int[] AuctionIds);
public record AdminResetPasswordRequest(string NewPassword);

[ApiController]
[Route("api/users")]
[Authorize(Roles = "SuperAdmin")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    public UsersController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _db.Users
            .Where(u => u.Role != UserRole.SuperAdmin)
            .Select(u => new
            {
                u.Id, u.Email, Role = u.Role.ToString(), u.DisplayName,
                AuctionIds = _db.AuctionUserAccess.Where(x => x.UserId == u.Id).Select(x => x.AuctionId).ToArray()
            }).ToListAsync();
        return Ok(users);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (email.Length < 3 || email.Any(char.IsWhiteSpace))
            return BadRequest(new { error = "Username or email must be at least 3 characters and cannot contain spaces" });
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { error = "Email already in use" });
        var passwordError = ValidatePassword(req.Password);
        if (passwordError != null) return BadRequest(new { error = passwordError });
        var validAuctionIds = await _db.Auctions.Where(a => req.AuctionIds.Contains(a.Id)).Select(a => a.Id).ToListAsync();

        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = UserRole.AuctionAdmin,
            DisplayName = req.DisplayName
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.AuctionUserAccess.AddRange(validAuctionIds.Select(id => new AuctionUserAccess { UserId = user.Id, AuctionId = id }));
        await _db.SaveChangesAsync();
        return Ok(new { user.Id, user.Email, Role = user.Role.ToString(), user.DisplayName });
    }

    [HttpPut("{id:int}/auctions")]
    public async Task<IActionResult> SetAuctionAccess(int id, UpdateUserAccessRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (user.Role == UserRole.SuperAdmin) return BadRequest(new { error = "SuperAdmin access cannot be restricted" });
        var old = await _db.AuctionUserAccess.Where(x => x.UserId == id).ToListAsync();
        _db.AuctionUserAccess.RemoveRange(old);
        var validIds = await _db.Auctions.Where(a => req.AuctionIds.Contains(a.Id)).Select(a => a.Id).ToListAsync();
        _db.AuctionUserAccess.AddRange(validIds.Select(a => new AuctionUserAccess { UserId = id, AuctionId = a }));
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id:int}/password")]
    public async Task<IActionResult> ResetPassword(int id, AdminResetPasswordRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (user.Role == UserRole.SuperAdmin) return BadRequest(new { error = "Change your own password from Account" });
        var passwordError = ValidatePassword(req.NewPassword);
        if (passwordError != null) return BadRequest(new { error = passwordError });
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static string? ValidatePassword(string password) =>
        string.IsNullOrWhiteSpace(password) || password.Length < 12 ||
        !password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
        !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch))
            ? "Password must be at least 12 characters and include uppercase, lowercase, a number, and a symbol"
            : null;
}
