using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Areas.Admin.Controllers;

[Authorize(Policy = Policies.Admin)]
public class UsersController : AdminController
{
    private readonly CmsDbContext _db;
    private readonly AuthService _auth;

    public UsersController(CmsDbContext db, AuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<IActionResult> Index(UserRole? role = null)
    {
        var query = _db.Users.AsNoTracking();
        if (role.HasValue) query = query.Where(u => u.Role == role);
        ViewBag.Role = role;
        ViewBag.PostCounts = await _db.Posts.Where(p => p.Type == PostType.Post && p.Status != PostStatus.Trash)
            .GroupBy(p => p.AuthorId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        return View(await query.OrderBy(u => u.UserName).ToListAsync());
    }

    [HttpGet]
    public IActionResult Create() => View("Edit", new UserEditForm());

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var u = await _db.Users.FindAsync(id);
        if (u == null) return NotFound();
        return View(new UserEditForm
        {
            Id = u.Id, UserName = u.UserName, Email = u.Email, DisplayName = u.DisplayName, Role = u.Role, IsActive = u.IsActive,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Save(UserEditForm form)
    {
        if (form.Id == 0 && string.IsNullOrEmpty(form.Password))
            ModelState.AddModelError(nameof(form.Password), "New users need a password.");
        if (await _db.Users.AnyAsync(u => u.UserName == form.UserName && u.Id != form.Id))
            ModelState.AddModelError(nameof(form.UserName), "That username is taken.");
        if (await _db.Users.AnyAsync(u => u.Email == form.Email && u.Id != form.Id))
            ModelState.AddModelError(nameof(form.Email), "Another account uses that email.");

        User? user = form.Id == 0 ? new User() : await _db.Users.FindAsync(form.Id);
        if (user == null) return NotFound();

        var losingAdmin = user.Role == UserRole.Administrator && (form.Role != UserRole.Administrator || !form.IsActive);
        if (form.Id != 0 && losingAdmin &&
            await _db.Users.CountAsync(u => u.Role == UserRole.Administrator && u.IsActive) <= 1)
            ModelState.AddModelError(nameof(form.Role), "This is the last active administrator.");

        if (!ModelState.IsValid) return View("Edit", form);

        var roleChanged = user.Role != form.Role || user.IsActive != form.IsActive;
        user.UserName = form.UserName.Trim();
        user.Email = form.Email.Trim();
        user.DisplayName = string.IsNullOrWhiteSpace(form.DisplayName) ? user.UserName : form.DisplayName.Trim();
        user.Role = form.Role;
        user.IsActive = form.IsActive;
        if (!string.IsNullOrEmpty(form.Password)) _auth.SetPassword(user, form.Password);
        else if (roleChanged) user.SecurityStamp = Guid.NewGuid().ToString("N");

        if (form.Id == 0) _db.Users.Add(user);
        await _db.SaveChangesAsync();
        Flash(form.Id == 0 ? $"User {user.UserName} created." : "User saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        if (id == CurrentUserId)
        {
            Flash("You can't delete yourself.", "error");
            return RedirectToAction(nameof(Index));
        }
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (user.Role == UserRole.Administrator && await _db.Users.CountAsync(u => u.Role == UserRole.Administrator) <= 1)
        {
            Flash("This is the last administrator.", "error");
            return RedirectToAction(nameof(Index));
        }

        // Keep their content: hand it over to the admin doing the delete.
        await _db.Posts.Where(p => p.AuthorId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.AuthorId, CurrentUserId));
        await _db.Comments.Where(c => c.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.UserId, (int?)null));
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        Flash($"Deleted {user.UserName}. Their posts now belong to you.");
        return RedirectToAction(nameof(Index));
    }
}

[Authorize(Policy = Policies.Admin)]
public class SettingsController : AdminController
{
    private readonly CmsDbContext _db;
    private readonly SettingsService _settings;

    public SettingsController(CmsDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<IActionResult> Index()
    {
        var s = await _settings.AllAsync();
        var form = new SettingsForm
        {
            SiteTitle = s[SettingKeys.SiteTitle],
            Tagline = s[SettingKeys.Tagline],
            FooterText = s[SettingKeys.FooterText],
            PostsPerPage = int.TryParse(s[SettingKeys.PostsPerPage], out var pp) ? pp : 10,
            HomePageId = int.TryParse(s[SettingKeys.HomePageId], out var hp) ? hp : null,
            CommentsEnabled = s[SettingKeys.CommentsEnabled] == "true",
            CommentModeration = s[SettingKeys.CommentModeration] == "true",
            AllowRegistration = s[SettingKeys.AllowRegistration] == "true",
        };
        await FillPages();
        return View(form);
    }

    [HttpPost]
    public async Task<IActionResult> Index(SettingsForm form)
    {
        if (!ModelState.IsValid)
        {
            await FillPages();
            return View(form);
        }

        await _settings.SetManyAsync(new Dictionary<string, string?>
        {
            [SettingKeys.SiteTitle] = form.SiteTitle.Trim(),
            [SettingKeys.Tagline] = form.Tagline?.Trim() ?? "",
            [SettingKeys.FooterText] = form.FooterText?.Trim() ?? "",
            [SettingKeys.PostsPerPage] = form.PostsPerPage.ToString(),
            [SettingKeys.HomePageId] = form.HomePageId?.ToString() ?? "",
            [SettingKeys.CommentsEnabled] = form.CommentsEnabled ? "true" : "false",
            [SettingKeys.CommentModeration] = form.CommentModeration ? "true" : "false",
            [SettingKeys.AllowRegistration] = form.AllowRegistration ? "true" : "false",
        });
        Flash("Settings saved.");
        return RedirectToAction(nameof(Index));
    }

    private async Task FillPages() =>
        ViewBag.Pages = await _db.Posts.AsNoTracking()
            .Where(p => p.Type == PostType.Page && p.Status == PostStatus.Published).OrderBy(p => p.Title).ToListAsync();
}

[Authorize(Policy = Policies.Admin)]
public class ThemesController : AdminController
{
    private readonly ThemeService _themes;
    private readonly SettingsService _settings;

    public ThemesController(ThemeService themes, SettingsService settings)
    {
        _themes = themes;
        _settings = settings;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Active = await _settings.GetAsync(SettingKeys.Theme);
        return View(_themes.List());
    }

    [HttpPost]
    public async Task<IActionResult> Activate(string folder)
    {
        if (!_themes.Exists(folder))
        {
            Flash("Theme not found.", "error");
            return RedirectToAction(nameof(Index));
        }
        await _settings.SetAsync(SettingKeys.Theme, folder);
        Flash($"Theme “{folder}” activated.");
        return RedirectToAction(nameof(Index));
    }
}
