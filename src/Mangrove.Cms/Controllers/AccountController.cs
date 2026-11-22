using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly CmsDbContext _db;
    private readonly AuthService _auth;
    private readonly SettingsService _settings;
    private readonly IPasswordHasher<User> _hasher;

    public AccountController(CmsDbContext db, AuthService auth, SettingsService settings, IPasswordHasher<User> hasher)
    {
        _db = db;
        _auth = auth;
        _settings = settings;
        _hasher = hasher;
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl) => View(new LoginForm { ReturnUrl = returnUrl });

    [HttpPost("login"), EnableRateLimiting("forms")]
    public async Task<IActionResult> Login(LoginForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var user = await _auth.ValidateAsync(form.Login, form.Password);
        if (user == null)
        {
            ModelState.AddModelError("", "Wrong username/email or password.");
            return View(form);
        }

        await AuthService.SignInAsync(HttpContext, user, form.RememberMe);
        if (!string.IsNullOrEmpty(form.ReturnUrl) && Url.IsLocalUrl(form.ReturnUrl)) return LocalRedirect(form.ReturnUrl);
        return user.Role >= UserRole.Author ? Redirect("/admin") : Redirect("/account/profile");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    [HttpGet("register")]
    public async Task<IActionResult> Register()
    {
        if (!await _settings.GetBoolAsync(SettingKeys.AllowRegistration)) return View("RegistrationClosed");
        return View(new RegisterForm());
    }

    [HttpPost("register"), EnableRateLimiting("forms")]
    public async Task<IActionResult> Register(RegisterForm form)
    {
        if (!await _settings.GetBoolAsync(SettingKeys.AllowRegistration)) return View("RegistrationClosed");
        if (await _db.Users.AnyAsync(u => u.UserName == form.UserName))
            ModelState.AddModelError(nameof(form.UserName), "That username is taken.");
        if (await _db.Users.AnyAsync(u => u.Email == form.Email))
            ModelState.AddModelError(nameof(form.Email), "An account with that email already exists.");
        if (!ModelState.IsValid) return View(form);

        var user = new User { UserName = form.UserName.Trim(), Email = form.Email.Trim(), DisplayName = form.UserName.Trim(), Role = UserRole.Subscriber };
        _auth.SetPassword(user, form.Password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await AuthService.SignInAsync(HttpContext, user, remember: false);
        return Redirect("/account/profile");
    }

    [Authorize, HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var user = await _db.Users.FindAsync(User.UserId());
        if (user == null) return Redirect("/account/login");
        ViewData["Saved"] = TempData["Saved"];
        return View(new ProfileForm { DisplayName = user.DisplayName, Email = user.Email, Bio = user.Bio });
    }

    [Authorize, HttpPost("profile")]
    public async Task<IActionResult> Profile(ProfileForm form)
    {
        var user = await _db.Users.FindAsync(User.UserId());
        if (user == null) return Redirect("/account/login");

        if (await _db.Users.AnyAsync(u => u.Email == form.Email && u.Id != user.Id))
            ModelState.AddModelError(nameof(form.Email), "Another account uses that email.");

        var changingPassword = !string.IsNullOrEmpty(form.NewPassword);
        if (changingPassword &&
            (string.IsNullOrEmpty(form.CurrentPassword) ||
             _hasher.VerifyHashedPassword(user, user.PasswordHash, form.CurrentPassword) == PasswordVerificationResult.Failed))
            ModelState.AddModelError(nameof(form.CurrentPassword), "Current password is wrong.");

        if (!ModelState.IsValid) return View(form);

        user.DisplayName = form.DisplayName?.Trim() ?? "";
        user.Email = form.Email.Trim();
        user.Bio = form.Bio;
        if (changingPassword) _auth.SetPassword(user, form.NewPassword!);
        await _db.SaveChangesAsync();

        // Re-issue the cookie so the new security stamp/display name apply immediately.
        await AuthService.SignInAsync(HttpContext, user, remember: true);
        TempData["Saved"] = "Profile saved.";
        return Redirect("/account/profile");
    }

    [HttpGet("denied")]
    public IActionResult Denied() => View();
}
