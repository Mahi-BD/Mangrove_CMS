using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Areas.Admin.Controllers;

/// <summary>Categories and tags.</summary>
[Authorize(Policy = Policies.Editor)]
public class TermsController : AdminController
{
    private readonly CmsDbContext _db;
    private readonly SlugService _slugs;

    public TermsController(CmsDbContext db, SlugService slugs)
    {
        _db = db;
        _slugs = slugs;
    }

    public async Task<IActionResult> Index(TermType type = TermType.Category, int? edit = null)
    {
        var terms = await _db.Terms.AsNoTracking().Where(t => t.Type == type).OrderBy(t => t.Name)
            .Select(t => new { Term = t, Count = t.PostTerms.Count })
            .ToListAsync();
        ViewBag.Type = type;
        ViewBag.Counts = terms.ToDictionary(x => x.Term.Id, x => x.Count);
        ViewBag.Editing = edit.HasValue ? terms.FirstOrDefault(x => x.Term.Id == edit)?.Term : null;
        return View(terms.Select(x => x.Term).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Save(int id, TermType type, string name, string? slug, string? description, int? parentId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Flash("Name is required.", "error");
            return RedirectToAction(nameof(Index), new { type });
        }

        var term = id == 0 ? new Term { Type = type } : await _db.Terms.FindAsync(id);
        if (term == null) return NotFound();
        if (id == 0) _db.Terms.Add(term);

        term.Name = name.Trim();
        term.Slug = await _slugs.UniqueTermSlugAsync(slug, name, term.Type, id == 0 ? null : id);
        term.Description = description?.Trim();
        term.ParentId = term.Type == TermType.Category && parentId is > 0 && parentId != id ? parentId : null;
        await _db.SaveChangesAsync();

        Flash(id == 0 ? $"Added “{term.Name}”." : "Saved.");
        return RedirectToAction(nameof(Index), new { type = term.Type });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var term = await _db.Terms.FindAsync(id);
        if (term == null) return NotFound();
        if (term.Type == TermType.Category && await _db.Terms.CountAsync(t => t.Type == TermType.Category) == 1)
        {
            Flash("Keep at least one category.", "error");
            return RedirectToAction(nameof(Index), new { type = term.Type });
        }
        _db.Terms.Remove(term);
        await _db.SaveChangesAsync();
        Flash($"Deleted “{term.Name}”.");
        return RedirectToAction(nameof(Index), new { type = term.Type });
    }
}

[Authorize(Policy = Policies.Editor)]
public class CommentsController : AdminController
{
    private readonly CmsDbContext _db;
    public CommentsController(CmsDbContext db) => _db = db;

    public async Task<IActionResult> Index(CommentStatus? status = null, int page = 1)
    {
        var all = _db.Comments.AsNoTracking();
        var counts = await all.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        ViewBag.Counts = counts.ToDictionary(c => c.Key, c => c.Count);
        ViewBag.Status = status;

        var query = status.HasValue ? all.Where(c => c.Status == status) : all.Where(c => c.Status == CommentStatus.Pending || c.Status == CommentStatus.Approved);
        const int size = 30;
        page = Math.Max(1, page);
        var total = await query.CountAsync();
        var items = await query.Include(c => c.Post).OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * size).Take(size).ToListAsync();
        return View(new PagedResult<Comment>(items, page, size, total));
    }

    [HttpPost]
    public async Task<IActionResult> SetStatus(int id, CommentStatus status, CommentStatus? from, string? returnTo)
    {
        var c = await _db.Comments.FindAsync(id);
        if (c == null) return NotFound();
        c.Status = status;
        await _db.SaveChangesAsync();
        Flash(status switch
        {
            CommentStatus.Approved => "Comment approved.",
            CommentStatus.Pending => "Comment moved back to pending.",
            CommentStatus.Spam => "Comment marked as spam.",
            _ => "Comment moved to the trash.",
        });
        if (!string.IsNullOrEmpty(returnTo) && Url.IsLocalUrl(returnTo)) return LocalRedirect(returnTo);
        return RedirectToAction(nameof(Index), new { status = from });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CommentStatus? from)
    {
        var c = await _db.Comments.FindAsync(id);
        if (c == null) return NotFound();
        _db.Comments.Remove(c);
        await _db.SaveChangesAsync();
        Flash("Comment deleted permanently.");
        return RedirectToAction(nameof(Index), new { status = from });
    }
}

[Authorize(Policy = Policies.Editor)]
public class MenusController : AdminController
{
    private readonly CmsDbContext _db;
    private readonly MenuService _menus;

    public MenusController(CmsDbContext db, MenuService menus)
    {
        _db = db;
        _menus = menus;
    }

    public async Task<IActionResult> Index(string location = "primary")
    {
        if (!MenuService.Locations.Contains(location)) location = "primary";
        ViewBag.Location = location;
        ViewBag.Items = await _menus.EditListAsync(location);
        ViewBag.Pages = await _db.Posts.AsNoTracking()
            .Where(p => p.Type == PostType.Page && p.Status == PostStatus.Published)
            .OrderBy(p => p.MenuOrder).ThenBy(p => p.Title).Select(p => new { p.Id, p.Title, p.Slug }).ToListAsync();
        ViewBag.Posts = await _db.Posts.AsNoTracking()
            .Where(p => p.Type == PostType.Post && p.Status == PostStatus.Published)
            .OrderByDescending(p => p.PublishedAt).Take(50).Select(p => new { p.Id, p.Title, p.Slug }).ToListAsync();
        ViewBag.Categories = await _db.Terms.AsNoTracking().Where(t => t.Type == TermType.Category)
            .OrderBy(t => t.Name).Select(t => new { t.Id, Title = t.Name, t.Slug }).ToListAsync();
        ViewBag.Tags = await _db.Terms.AsNoTracking().Where(t => t.Type == TermType.Tag)
            .OrderBy(t => t.Name).Select(t => new { t.Id, Title = t.Name, t.Slug }).ToListAsync();
        ViewBag.Counts = await _db.MenuItems.GroupBy(m => m.Location).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        return View();
    }

    /// <summary>Saves the whole menu from the drag-and-drop editor.</summary>
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] MenuSaveModel model)
    {
        var error = await _menus.SaveAsync(model);
        if (error != null) return BadRequest(new { error });
        return Json(new { ok = true, items = await _menus.EditListAsync(model.Location) });
    }
}
