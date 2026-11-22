using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.Author)]
public abstract class AdminController : Controller
{
    protected void Flash(string message, string kind = "success")
    {
        TempData["Flash"] = message;
        TempData["FlashKind"] = kind;
    }

    protected int CurrentUserId => User.UserId() ?? 0;
}

public class DashboardController : AdminController
{
    private readonly CmsDbContext _db;
    public DashboardController(CmsDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var mine = !User.AtLeast(UserRole.Editor);
        var uid = CurrentUserId;
        var posts = _db.Posts.Where(p => p.Type == PostType.Post && (!mine || p.AuthorId == uid));

        var vm = new DashboardViewModel
        {
            Posts = await posts.CountAsync(p => p.Status == PostStatus.Published || p.Status == PostStatus.Scheduled),
            Drafts = await posts.CountAsync(p => p.Status == PostStatus.Draft),
            Pages = await _db.Posts.CountAsync(p => p.Type == PostType.Page && p.Status != PostStatus.Trash),
            PendingComments = await _db.Comments.CountAsync(c => c.Status == CommentStatus.Pending),
            Users = await _db.Users.CountAsync(),
            MediaFiles = await _db.Media.CountAsync(),
            RecentPosts = await posts.Where(p => p.Status != PostStatus.Trash)
                .OrderByDescending(p => p.UpdatedAt).Take(6).ToListAsync(),
            RecentComments = User.AtLeast(UserRole.Editor)
                ? await _db.Comments.Include(c => c.Post).OrderByDescending(c => c.CreatedAt).Take(6).ToListAsync()
                : new(),
        };
        return View(vm);
    }
}
