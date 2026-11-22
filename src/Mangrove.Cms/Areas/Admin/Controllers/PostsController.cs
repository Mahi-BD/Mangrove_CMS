using System.Text.Json;
using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Areas.Admin.Controllers;

/// <summary>Posts and pages share one editor. Authors manage their own posts; pages need Editor.</summary>
public class PostsController : AdminController
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly CmsDbContext _db;
    private readonly SlugService _slugs;
    private readonly ContentRenderer _renderer;
    private readonly TableService _tables;
    private readonly NoticeService _notices;

    public PostsController(CmsDbContext db, SlugService slugs, ContentRenderer renderer, TableService tables, NoticeService notices)
    {
        _db = db;
        _slugs = slugs;
        _renderer = renderer;
        _tables = tables;
        _notices = notices;
    }

    private bool CanEdit(Post p) =>
        User.AtLeast(UserRole.Editor) || (p.Type == PostType.Post && p.AuthorId == CurrentUserId);

    private bool CanUseType(PostType type) => type == PostType.Post || User.AtLeast(UserRole.Editor);

    public async Task<IActionResult> Index(PostType type = PostType.Post, PostStatus? status = null, string? q = null, int page = 1)
    {
        if (!CanUseType(type)) return Forbid();

        var query = _db.Posts.AsNoTracking().Where(p => p.Type == type);
        if (!User.AtLeast(UserRole.Editor)) query = query.Where(p => p.AuthorId == CurrentUserId);

        var counts = await query.GroupBy(p => p.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        ViewBag.Counts = counts.ToDictionary(c => c.Key, c => c.Count);

        query = status.HasValue ? query.Where(p => p.Status == status) : query.Where(p => p.Status != PostStatus.Trash);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => EF.Functions.Like(p.Title, $"%{q.Trim()}%"));

        const int size = 20;
        page = Math.Max(1, page);
        var total = await query.CountAsync();
        var items = await query.Include(p => p.Author)
            .Include(p => p.PostTerms).ThenInclude(pt => pt.Term)
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * size).Take(size)
            .AsSplitQuery().ToListAsync();

        ViewBag.Type = type;
        ViewBag.Status = status;
        ViewBag.Q = q;
        return View(new PagedResult<Post>(items, page, size, total));
    }

    [HttpGet]
    public async Task<IActionResult> Create(PostType type = PostType.Post, PageKind? kind = null)
    {
        if (!CanUseType(type)) return Forbid();
        // New pages start by choosing what kind of page they are.
        if (type == PostType.Page && kind == null) return View("ChooseKind");

        var form = new PostEditForm { Type = type, Kind = type == PostType.Page ? kind ?? PageKind.Standard : PageKind.Standard, AllowComments = type == PostType.Post };
        if (type == PostType.Post)
        {
            var general = await _db.Terms.FirstOrDefaultAsync(t => t.Type == TermType.Category);
            if (general != null) form.CategoryIds.Add(general.Id);
        }
        await FillLookups(form);
        return View("Edit", form);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var post = await _db.Posts.Include(p => p.PostTerms).ThenInclude(pt => pt.Term).FirstOrDefaultAsync(p => p.Uid == id);
        if (post == null) return NotFound();
        if (!CanEdit(post)) return Forbid();

        var form = new PostEditForm
        {
            Id = post.Id,
            Uid = post.Uid,
            Type = post.Type,
            Kind = post.Kind,
            Title = post.Title,
            Slug = post.Slug,
            Content = post.Content,
            Excerpt = post.Excerpt,
            MetaDescription = post.MetaDescription,
            Status = post.Status,
            PublishedAt = post.PublishedAt,
            FeaturedMediaId = post.FeaturedMediaId,
            ParentId = post.ParentId,
            MenuOrder = post.MenuOrder,
            AllowComments = post.AllowComments,
            IsSticky = post.IsSticky,
            CategoryIds = post.PostTerms.Where(pt => pt.Term!.Type == TermType.Category).Select(pt => pt.TermId).ToList(),
            Tags = string.Join(", ", post.PostTerms.Where(pt => pt.Term!.Type == TermType.Tag).Select(pt => pt.Term!.Name)),
            ListPageSize = post.ListPageSize,
            ListSearchable = post.ListSearchable,
        };
        if (post.Kind == PageKind.Table) form.TableJson = await TableJsonAsync(post.Id);
        if (post.Kind == PageKind.Notices) form.NoticesJson = await NoticesJsonAsync(post.Id);
        await FillLookups(form);
        return View(form);
    }

    [HttpPost]
    public async Task<IActionResult> Save(PostEditForm form)
    {
        if (!CanUseType(form.Type)) return Forbid();

        Post? post = null;
        if (form.Uid != Guid.Empty)
        {
            post = await _db.Posts.Include(p => p.PostTerms).FirstOrDefaultAsync(p => p.Uid == form.Uid);
            if (post == null) return NotFound();
            form.Id = post.Id;
            if (!CanEdit(post)) return Forbid();
            form.Type = post.Type; // type can't change via form tampering
        }

        if (form.Status == PostStatus.Scheduled && (form.PublishedAt == null || form.PublishedAt <= DateTime.UtcNow))
            ModelState.AddModelError(nameof(form.PublishedAt), "Pick a future date to schedule.");
        if (form.ParentId != null && form.ParentId == form.Id)
            ModelState.AddModelError(nameof(form.ParentId), "A page can't be its own parent.");

        var kind = post?.Kind ?? (form.Type == PostType.Page && Enum.IsDefined(form.Kind) ? form.Kind : PageKind.Standard);
        form.Kind = kind;
        TableSaveModel? table = null;
        List<NoticeSaveModel>? notices = null;
        try
        {
            if (kind == PageKind.Table) table = JsonSerializer.Deserialize<TableSaveModel>(form.TableJson ?? "{}", JsonOpts) ?? new();
            if (kind == PageKind.Notices) notices = JsonSerializer.Deserialize<List<NoticeSaveModel>>(form.NoticesJson ?? "[]", JsonOpts) ?? new();
        }
        catch (JsonException)
        {
            ModelState.AddModelError("", "The table or notices couldn't be read. Reload the page and try again.");
        }

        if (!ModelState.IsValid)
        {
            await FillLookups(form);
            return View("Edit", form);
        }

        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync();
        if (post == null)
        {
            post = new Post
            {
                Type = form.Type,
                Kind = kind,
                AuthorId = CurrentUserId,
                CreatedAt = now,
                AllowComments = false,
            };
            _db.Posts.Add(post);
        }

        post.Title = form.Title.Trim();
        post.Slug = await _slugs.UniquePostSlugAsync(form.Slug, form.Title, post.Type, post.Id == 0 ? null : post.Id);
        post.Content = form.Content ?? "";
        post.Excerpt = string.IsNullOrWhiteSpace(form.Excerpt) ? null : form.Excerpt.Trim();
        post.MetaDescription = string.IsNullOrWhiteSpace(form.MetaDescription) ? null : form.MetaDescription.Trim();
        post.FeaturedMediaId = form.FeaturedMediaId is > 0 && await _db.Media.AnyAsync(m => m.Id == form.FeaturedMediaId)
            ? form.FeaturedMediaId : null;
        post.AllowComments = kind == PageKind.Standard && form.AllowComments;
        post.UpdatedAt = now;
        if (kind != PageKind.Standard)
        {
            post.ListPageSize = Math.Clamp(form.ListPageSize, 0, 500);
            post.ListSearchable = form.ListSearchable;
        }

        if (post.Type == PostType.Page)
        {
            post.ParentId = form.ParentId is > 0 ? form.ParentId : null;
            post.MenuOrder = form.MenuOrder;
        }
        else
        {
            post.IsSticky = form.IsSticky && User.AtLeast(UserRole.Editor);
        }

        post.Status = form.Status == PostStatus.Trash ? PostStatus.Draft : form.Status;
        post.PublishedAt = post.Status switch
        {
            PostStatus.Scheduled => form.PublishedAt,
            PostStatus.Published => form.PublishedAt ?? post.PublishedAt ?? now,
            _ => form.PublishedAt,
        };

        if (post.Type == PostType.Post) await ApplyTermsAsync(post, form);

        await _db.SaveChangesAsync();

        // The table or notices are saved with the page, in the same transaction.
        var contentError = table != null ? await _tables.SaveAsync(post.Id, table)
            : notices != null ? await _notices.SyncAsync(post.Id, notices, CurrentUserId) : null;
        if (contentError != null)
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            ModelState.AddModelError("", contentError);
            await FillLookups(form);
            return View("Edit", form);
        }
        await tx.CommitAsync();

        Flash(post.Status switch
        {
            PostStatus.Published => $"{post.Type} published.",
            PostStatus.Scheduled => $"{post.Type} scheduled for {post.PublishedAt:yyyy-MM-dd HH:mm} UTC.",
            _ => "Draft saved.",
        });

        return RedirectToAction(nameof(Edit), new { id = post.Uid });
    }

    private async Task ApplyTermsAsync(Post post, PostEditForm form)
    {
        var validCats = await _db.Terms.Where(t => t.Type == TermType.Category && form.CategoryIds.Contains(t.Id))
            .Select(t => t.Id).ToListAsync();

        var tagNames = (form.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => n.Length <= 120).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
        var tagIds = new List<int>();
        foreach (var name in tagNames)
        {
            var slug = Slug.From(name);
            if (slug.Length == 0) continue;
            var tag = await _db.Terms.FirstOrDefaultAsync(t => t.Type == TermType.Tag && t.Slug == slug)
                      ?? _db.Terms.Local.FirstOrDefault(t => t.Type == TermType.Tag && t.Slug == slug);
            if (tag == null)
            {
                tag = new Term { Type = TermType.Tag, Name = name, Slug = slug };
                _db.Terms.Add(tag);
                await _db.SaveChangesAsync();
            }
            tagIds.Add(tag.Id);
        }

        var wanted = validCats.Concat(tagIds).ToHashSet();
        post.PostTerms.RemoveAll(pt => !wanted.Contains(pt.TermId));
        foreach (var id in wanted.Where(id => post.PostTerms.All(pt => pt.TermId != id)))
            post.PostTerms.Add(new PostTerm { TermId = id });
    }

    [HttpPost]
    public async Task<IActionResult> Trash(Guid id)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(p => p.Uid == id);
        if (post == null) return NotFound();
        if (!CanEdit(post)) return Forbid();
        post.Status = PostStatus.Trash;
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        Flash($"“{post.Title}” moved to the trash.");
        return RedirectToAction(nameof(Index), new { type = post.Type });
    }

    [HttpPost]
    public async Task<IActionResult> Restore(Guid id)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(p => p.Uid == id);
        if (post == null) return NotFound();
        if (!CanEdit(post)) return Forbid();
        post.Status = PostStatus.Draft;
        await _db.SaveChangesAsync();
        Flash($"“{post.Title}” restored as a draft.");
        return RedirectToAction(nameof(Index), new { type = post.Type, status = PostStatus.Trash });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(p => p.Uid == id);
        if (post == null) return NotFound();
        if (!CanEdit(post)) return Forbid();
        if (post.Status != PostStatus.Trash)
        {
            Flash("Move it to the trash first.", "error");
            return RedirectToAction(nameof(Index), new { type = post.Type });
        }
        _db.Posts.Remove(post);
        await _db.SaveChangesAsync();
        Flash("Deleted permanently.");
        return RedirectToAction(nameof(Index), new { type = post.Type, status = PostStatus.Trash });
    }

    /// <summary>Live preview for the Markdown editor.</summary>
    [HttpPost]
    public IActionResult Preview([FromForm] string? content) =>
        Content(_renderer.Render(content).Value ?? "", "text/html; charset=utf-8");

    private async Task<string> TableJsonAsync(int postId)
    {
        var (cols, rows) = await _tables.LoadAsync(postId);
        return JsonSerializer.Serialize(new
        {
            columns = cols.Select(c => new { key = c.Id.ToString(), name = c.Name, type = (int)c.Type, width = c.Width }),
            rows = rows.Select(r => new { cells = TableService.ReadCells(r.Data) }),
        }, JsonOpts);
    }

    private async Task<string> NoticesJsonAsync(int postId)
    {
        var list = await _db.Notices.AsNoTracking().Include(n => n.Attachments).ThenInclude(a => a.Media)
            .Where(n => n.PostId == postId)
            .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.NoticeDate).ThenByDescending(n => n.Id)
            .ToListAsync();
        return JsonSerializer.Serialize(list.Select(n => new
        {
            id = n.Id,
            title = n.Title,
            date = n.NoticeDate.ToString("yyyy-MM-dd"),
            description = n.Description,
            pinned = n.IsPinned,
            published = n.IsPublished,
            files = n.Attachments.OrderBy(a => a.SortOrder).Where(a => a.Media != null)
                .Select(a => new { mediaId = a.MediaId, fileName = a.Media!.FileName, size = a.Media.SizeBytes, url = a.Media.Url }),
        }), JsonOpts);
    }

    private async Task FillLookups(PostEditForm form)
    {
        ViewBag.Categories = await _db.Terms.AsNoTracking().Where(t => t.Type == TermType.Category).OrderBy(t => t.Name).ToListAsync();
        ViewBag.Pages = form.Type == PostType.Page
            ? await _db.Posts.AsNoTracking().Where(p => p.Type == PostType.Page && p.Id != form.Id && p.Status != PostStatus.Trash)
                .OrderBy(p => p.Title).ToListAsync()
            : new List<Post>();
        ViewBag.Featured = form.FeaturedMediaId is > 0 ? await _db.Media.FindAsync(form.FeaturedMediaId) : null;
        if (form.Kind == PageKind.Table) form.TableJson ??= JsonSerializer.Serialize(new TableSaveModel(), JsonOpts);
        if (form.Kind == PageKind.Notices) form.NoticesJson ??= "[]";
    }
}
