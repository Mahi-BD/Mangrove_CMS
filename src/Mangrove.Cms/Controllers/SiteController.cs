using System.Text;
using System.Xml;
using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Controllers;

public class SiteController : Controller
{
    private readonly CmsDbContext _db;
    private readonly PostQueryService _posts;
    private readonly SettingsService _settings;
    private readonly ContentRenderer _renderer;

    public SiteController(CmsDbContext db, PostQueryService posts, SettingsService settings, ContentRenderer renderer)
    {
        _db = db;
        _posts = posts;
        _settings = settings;
        _renderer = renderer;
    }

    private Task<int> PageSizeAsync() => _settings.GetIntAsync(SettingKeys.PostsPerPage, 10);

    [HttpGet("/")]
    public async Task<IActionResult> Index(int page = 1)
    {
        // A static page can be chosen as the homepage in Settings → Reading.
        if (page == 1 && int.TryParse(await _settings.GetAsync(SettingKeys.HomePageId), out var homeId))
        {
            var home = await _posts.Live(PostType.Page).FirstOrDefaultAsync(p => p.Id == homeId);
            if (home != null) return await RenderSingle(home, isHome: true);
        }

        var result = await _posts.PageAsync(_posts.Live(PostType.Post), page, await PageSizeAsync(), stickyFirst: true);
        return View("Index", new PostListViewModel
        {
            Heading = await _settings.GetAsync(SettingKeys.SiteTitle),
            Description = await _settings.GetAsync(SettingKeys.Tagline),
            Result = result,
            PageUrlFormat = "/?page={0}",
            IsHome = true,
        });
    }

    [HttpGet("/blog")]
    public async Task<IActionResult> Blog(int page = 1)
    {
        var result = await _posts.PageAsync(_posts.Live(PostType.Post), page, await PageSizeAsync(), stickyFirst: true);
        return View("Index", new PostListViewModel { Heading = "Blog", Result = result, PageUrlFormat = "/blog?page={0}" });
    }

    [HttpGet("/category/{slug}")]
    public Task<IActionResult> Category(string slug, int page = 1) => TermArchive(TermType.Category, slug, page);

    [HttpGet("/tag/{slug}")]
    public Task<IActionResult> Tag(string slug, int page = 1) => TermArchive(TermType.Tag, slug, page);

    private async Task<IActionResult> TermArchive(TermType type, string slug, int page)
    {
        var term = await _db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Type == type && t.Slug == slug);
        if (term == null) return NotFoundPage();

        var query = _posts.Live(PostType.Post).Where(p => p.PostTerms.Any(pt => pt.TermId == term.Id));
        var result = await _posts.PageAsync(query, page, await PageSizeAsync());
        var prefix = type == TermType.Category ? "category" : "tag";
        return View("Index", new PostListViewModel
        {
            Heading = (type == TermType.Category ? "Category: " : "Tag: ") + term.Name,
            Description = term.Description,
            Result = result,
            PageUrlFormat = $"/{prefix}/{Uri.EscapeDataString(term.Slug)}?page={{0}}",
        });
    }

    [HttpGet("/author/{username}")]
    public async Task<IActionResult> Author(string username, int page = 1)
    {
        var author = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == username);
        if (author == null) return NotFoundPage();

        var result = await _posts.PageAsync(_posts.Live(PostType.Post).Where(p => p.AuthorId == author.Id), page, await PageSizeAsync());
        return View("Index", new PostListViewModel
        {
            Heading = "Author: " + (string.IsNullOrWhiteSpace(author.DisplayName) ? author.UserName : author.DisplayName),
            Description = author.Bio,
            Result = result,
            PageUrlFormat = $"/author/{Uri.EscapeDataString(author.UserName)}?page={{0}}",
        });
    }

    [HttpGet("/search")]
    public async Task<IActionResult> Search(string? q, int page = 1)
    {
        q = (q ?? "").Trim();
        var result = q.Length == 0
            ? new PagedResult<Post>(Array.Empty<Post>(), 1, 10, 0)
            : await _posts.PageAsync(_posts.Search(q), page, await PageSizeAsync());
        return View("Index", new PostListViewModel
        {
            Heading = q.Length == 0 ? "Search" : $"Search results for “{q}”",
            Result = result,
            SearchQuery = q,
            PageUrlFormat = $"/search?q={Uri.EscapeDataString(q)}&page={{0}}",
        });
    }

    /// <summary>Pages and posts both live at /{slug}. Nested pages use /parent/child.</summary>
    [HttpGet("/{**path}", Order = int.MaxValue)]
    public async Task<IActionResult> Single(string path)
    {
        var slug = path.TrimEnd('/').Split('/').LastOrDefault() ?? "";
        if (slug.Length == 0) return NotFoundPage();

        var page = await _posts.Live(PostType.Page).Include(p => p.Author).FirstOrDefaultAsync(p => p.Slug == slug);
        if (page != null) return await RenderSingle(page);

        var post = await _posts.Live(PostType.Post).FirstOrDefaultAsync(p => p.Slug == slug);
        if (post != null) return await RenderSingle(post);

        // Editors can preview drafts at their real URL.
        if (User.AtLeast(UserRole.Author))
        {
            var draft = await _db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.Status != PostStatus.Trash);
            if (draft != null && (User.AtLeast(UserRole.Editor) || draft.AuthorId == User.UserId()))
                return await RenderSingle(draft);
        }
        return NotFoundPage();
    }

    private async Task<IActionResult> RenderSingle(Post post, bool isHome = false)
    {
        var full = await _db.Posts.AsNoTracking()
            .Include(p => p.Author).Include(p => p.FeaturedMedia)
            .Include(p => p.PostTerms).ThenInclude(pt => pt.Term)
            .AsSplitQuery()
            .FirstAsync(p => p.Id == post.Id);

        var commentsEnabled = await _settings.GetBoolAsync(SettingKeys.CommentsEnabled);
        var vm = new SingleViewModel
        {
            Post = full,
            Body = _renderer.Render(full.Content),
            CommentsOpen = commentsEnabled && full.AllowComments && full.Type == PostType.Post && full.IsLive(DateTime.UtcNow),
            CommentForm = new CommentForm { PostId = full.Id },
            Flash = TempData["Flash"] as string,
        };

        if (full.Type == PostType.Post)
        {
            vm.Comments = await _db.Comments.AsNoTracking()
                .Where(c => c.PostId == full.Id && c.Status == CommentStatus.Approved)
                .OrderBy(c => c.CreatedAt).ToListAsync();
            var live = _posts.Live(PostType.Post);
            vm.Previous = await live.Where(p => p.PublishedAt < full.PublishedAt).OrderByDescending(p => p.PublishedAt).FirstOrDefaultAsync();
            vm.Next = await live.Where(p => p.PublishedAt > full.PublishedAt).OrderBy(p => p.PublishedAt).FirstOrDefaultAsync();
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            vm.CommentForm.AuthorName = User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? "";
            vm.CommentForm.AuthorEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        }

        ViewData["IsHome"] = isHome;
        return View("Single", vm);
    }

    [HttpPost("/comments"), EnableRateLimiting("forms")]
    public async Task<IActionResult> PostComment(CommentForm form)
    {
        var post = await _posts.Live(PostType.Post).FirstOrDefaultAsync(p => p.Id == form.PostId);
        if (post == null) return NotFoundPage();

        if (!string.IsNullOrEmpty(form.Website2)) return Redirect("/" + post.Slug); // bot
        if (!post.AllowComments || !await _settings.GetBoolAsync(SettingKeys.CommentsEnabled))
            return Redirect("/" + post.Slug);

        if (!ModelState.IsValid)
        {
            TempData["Flash"] = "Please fill in your name and a comment.";
            return Redirect($"/{post.Slug}#respond");
        }

        var moderated = await _settings.GetBoolAsync(SettingKeys.CommentModeration);
        var trusted = User.AtLeast(UserRole.Author);
        var parentOk = form.ParentId == null ||
                       await _db.Comments.AnyAsync(c => c.Id == form.ParentId && c.PostId == post.Id);

        _db.Comments.Add(new Comment
        {
            PostId = post.Id,
            ParentId = parentOk ? form.ParentId : null,
            UserId = User.UserId(),
            AuthorName = form.AuthorName.Trim(),
            AuthorEmail = form.AuthorEmail?.Trim(),
            AuthorUrl = form.AuthorUrl?.Trim(),
            Content = form.Content.Trim(),
            Status = moderated && !trusted ? CommentStatus.Pending : CommentStatus.Approved,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await _db.SaveChangesAsync();

        TempData["Flash"] = moderated && !trusted
            ? "Thanks! Your comment is awaiting moderation."
            : "Thanks for your comment!";
        return Redirect($"/{post.Slug}#comments");
    }

    [HttpGet("/feed")]
    [ResponseCache(Duration = 300)]
    public async Task<IActionResult> Feed()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var posts = await _posts.Live(PostType.Post).Include(p => p.Author)
            .OrderByDescending(p => p.PublishedAt).Take(20).ToListAsync();

        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
        {
            w.WriteStartElement("rss");
            w.WriteAttributeString("version", "2.0");
            w.WriteStartElement("channel");
            w.WriteElementString("title", await _settings.GetAsync(SettingKeys.SiteTitle));
            w.WriteElementString("link", baseUrl + "/");
            w.WriteElementString("description", await _settings.GetAsync(SettingKeys.Tagline));
            w.WriteElementString("generator", "Mangrove CMS");
            foreach (var p in posts)
            {
                w.WriteStartElement("item");
                w.WriteElementString("title", p.Title);
                w.WriteElementString("link", $"{baseUrl}/{p.Slug}");
                w.WriteElementString("guid", $"{baseUrl}/{p.Slug}");
                w.WriteElementString("pubDate", (p.PublishedAt ?? p.CreatedAt).ToString("R"));
                w.WriteElementString("author", p.Author?.DisplayName ?? p.Author?.UserName ?? "");
                w.WriteElementString("description", string.IsNullOrWhiteSpace(p.Excerpt) ? _renderer.PlainText(p.Content, 400) : p.Excerpt);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }
        return Content(sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\""), "application/rss+xml; charset=utf-8");
    }

    [HttpGet("/sitemap.xml")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Sitemap()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var items = await _posts.Live(PostType.Page).Select(p => new { p.Slug, p.UpdatedAt })
            .Concat(_posts.Live(PostType.Post).Select(p => new { p.Slug, p.UpdatedAt }))
            .ToListAsync();

        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true }))
        {
            w.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            w.WriteStartElement("url");
            w.WriteElementString("loc", baseUrl + "/");
            w.WriteEndElement();
            foreach (var i in items)
            {
                w.WriteStartElement("url");
                w.WriteElementString("loc", $"{baseUrl}/{Uri.EscapeDataString(i.Slug)}");
                w.WriteElementString("lastmod", i.UpdatedAt.ToString("yyyy-MM-dd"));
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        return Content(sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\""), "application/xml; charset=utf-8");
    }

    [HttpGet("/robots.txt")]
    public IActionResult Robots() =>
        Content($"User-agent: *\nDisallow: /admin/\nDisallow: /account/\n\nSitemap: {Request.Scheme}://{Request.Host}/sitemap.xml\n", "text/plain");

    [Route("/error/{code:int?}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Error(int? code)
    {
        if (code == 404) return NotFoundPage();
        Response.StatusCode = code ?? 500;
        ViewData["Code"] = Response.StatusCode;
        _ = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        return View("Error");
    }

    private IActionResult NotFoundPage()
    {
        Response.StatusCode = 404;
        return View("NotFound");
    }
}
