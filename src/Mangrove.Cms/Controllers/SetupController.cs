using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Controllers;

/// <summary>First-run wizard: site title + the first administrator. Disabled once installed.</summary>
[Route("setup")]
public class SetupController : Controller
{
    private readonly CmsDbContext _db;
    private readonly AuthService _auth;
    private readonly SettingsService _settings;

    public SetupController(CmsDbContext db, AuthService auth, SettingsService settings)
    {
        _db = db;
        _auth = auth;
        _settings = settings;
    }

    [HttpGet("")]
    public IActionResult Index() => View(new SetupForm());

    [HttpPost("")]
    public async Task<IActionResult> Index(SetupForm form)
    {
        if (await _settings.GetBoolAsync(SettingKeys.Installed) || await _db.Users.AnyAsync())
            return Redirect("/");
        if (!ModelState.IsValid) return View(form);

        var admin = new User
        {
            UserName = form.UserName.Trim(),
            Email = form.Email.Trim(),
            DisplayName = form.UserName.Trim(),
            Role = UserRole.Administrator,
        };
        _auth.SetPassword(admin, form.Password);
        _db.Users.Add(admin);

        var general = new Term { Type = TermType.Category, Name = "General", Slug = "general" };
        _db.Terms.Add(general);
        await _db.SaveChangesAsync();

        if (form.SampleContent) await SeedSampleAsync(admin, general);

        await _settings.SetManyAsync(new Dictionary<string, string?>
        {
            [SettingKeys.SiteTitle] = form.SiteTitle.Trim(),
            [SettingKeys.Tagline] = form.Tagline?.Trim() ?? "",
            [SettingKeys.Installed] = "true",
        });

        await AuthService.SignInAsync(HttpContext, admin, remember: true);
        return Redirect("/admin");
    }

    private async Task SeedSampleAsync(User admin, Term general)
    {
        var now = DateTime.UtcNow;
        var hello = new Post
        {
            Type = PostType.Post,
            Status = PostStatus.Published,
            Title = "Hello world!",
            Slug = "hello-world",
            Content = """
                Welcome to **Mangrove CMS**. This is your first post. Edit or delete it, then start writing!

                Posts are written in [Markdown](https://commonmark.org/help/). You can use:

                - **bold**, *italic* and `code`
                - lists, tables, block quotes and headings
                - images from the Media library

                > Mangrove trees thrive where land meets sea. Your content can too.
                """,
            Excerpt = "Welcome to Mangrove CMS. This is your first post.",
            AuthorId = admin.Id,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        hello.PostTerms.Add(new PostTerm { TermId = general.Id });

        var about = new Post
        {
            Type = PostType.Page,
            Status = PostStatus.Published,
            Title = "About",
            Slug = "about",
            Content = "This is an example page. Unlike posts, pages sit outside the blog timeline. Use them for things like *About* or *Contact*.",
            AuthorId = admin.Id,
            AllowComments = false,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Posts.AddRange(hello, about);
        await _db.SaveChangesAsync();

        _db.Comments.Add(new Comment
        {
            PostId = hello.Id,
            AuthorName = "A Mangrove commenter",
            Content = "Hi, this is a comment. You can approve, edit or delete comments from the dashboard.",
            Status = CommentStatus.Approved,
        });
        _db.MenuItems.AddRange(
            new MenuItem { Location = "primary", Label = "Home", Url = "/", SortOrder = 0 },
            new MenuItem { Location = "primary", Label = "About", Url = "/about", SortOrder = 1 });
        await _db.SaveChangesAsync();
    }
}
