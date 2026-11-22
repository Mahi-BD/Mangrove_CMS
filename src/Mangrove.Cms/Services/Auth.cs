using System.Security.Claims;
using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Services;

public static class Policies
{
    public const string Admin = "Admin";   // Administrator
    public const string Editor = "Editor"; // Editor+
    public const string Author = "Author"; // Author+ (can enter the dashboard)
}

public static class ClaimsExtensions
{
    public const string StampClaim = "mangrove:stamp";

    public static int? UserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static UserRole Role(this ClaimsPrincipal user) =>
        Enum.TryParse<UserRole>(user.FindFirstValue(ClaimTypes.Role), out var r) ? r : UserRole.Subscriber;

    public static bool AtLeast(this ClaimsPrincipal user, UserRole role) =>
        user.Identity?.IsAuthenticated == true && user.Role() >= role;
}

public class AuthService
{
    private readonly CmsDbContext _db;
    private readonly IPasswordHasher<User> _hasher;

    public AuthService(CmsDbContext db, IPasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public void SetPassword(User user, string password)
    {
        user.PasswordHash = _hasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public async Task<User?> ValidateAsync(string login, string password)
    {
        login = login.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == login || u.Email == login);
        if (user == null || !user.IsActive)
        {
            // Spend similar time as a real check so usernames can't be probed by timing.
            _hasher.HashPassword(new User(), password);
            return null;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = _hasher.HashPassword(user, password);

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return user;
    }

    public static ClaimsPrincipal BuildPrincipal(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.GivenName, string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(StampClaimType, user.SecurityStamp),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private const string StampClaimType = ClaimsExtensions.StampClaim;

    public static Task SignInAsync(HttpContext http, User user, bool remember) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, BuildPrincipal(user),
            new AuthenticationProperties { IsPersistent = remember });
}

/// <summary>Re-checks each cookie against the database (every few minutes) so role changes,
/// password resets and deactivations take effect without waiting for the cookie to expire.</summary>
public class StampValidatingCookieEvents : CookieAuthenticationEvents
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const string CheckedKey = "mangrove.checked";

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var props = context.Properties;
        if (props.Items.TryGetValue(CheckedKey, out var last) &&
            DateTimeOffset.TryParse(last, out var when) && DateTimeOffset.UtcNow - when < Interval)
            return;

        var id = context.Principal?.UserId();
        var stamp = context.Principal?.FindFirstValue(ClaimsExtensions.StampClaim);
        var db = context.HttpContext.RequestServices.GetRequiredService<CmsDbContext>();
        var user = id == null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);

        if (user == null || !user.IsActive || user.SecurityStamp != stamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        if (user.Role.ToString() != context.Principal!.FindFirstValue(ClaimTypes.Role) ||
            user.DisplayName != context.Principal!.FindFirstValue(ClaimTypes.GivenName))
            context.ReplacePrincipal(AuthService.BuildPrincipal(user));

        props.Items[CheckedKey] = DateTimeOffset.UtcNow.ToString("O");
        context.ShouldRenew = true;
    }
}
