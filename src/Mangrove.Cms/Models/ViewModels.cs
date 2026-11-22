using System.ComponentModel.DataAnnotations;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Html;

namespace Mangrove.Cms.Models;

// ---------- Public site ----------

public class PostListViewModel
{
    public string Heading { get; set; } = "";
    public string? Description { get; set; }
    public PagedResult<Post> Result { get; set; } = new(Array.Empty<Post>(), 1, 10, 0);
    /// <summary>URL for page N, e.g. "/category/news?page={0}".</summary>
    public string PageUrlFormat { get; set; } = "/?page={0}";
    public string? SearchQuery { get; set; }
    public bool IsHome { get; set; }
}

public class SingleViewModel
{
    public Post Post { get; set; } = null!;
    public HtmlString Body { get; set; } = HtmlString.Empty;
    public List<Comment> Comments { get; set; } = new();
    public bool CommentsOpen { get; set; }
    public CommentForm CommentForm { get; set; } = new();
    public Post? Previous { get; set; }
    public Post? Next { get; set; }
    public string? Flash { get; set; }
}

public class CommentForm
{
    public int PostId { get; set; }
    public int? ParentId { get; set; }

    [Required, MaxLength(120), Display(Name = "Name")]
    public string AuthorName { get; set; } = "";

    [EmailAddress, MaxLength(200), Display(Name = "Email (not published)")]
    public string? AuthorEmail { get; set; }

    [Url, MaxLength(300), Display(Name = "Website")]
    public string? AuthorUrl { get; set; }

    [Required, MaxLength(5000), Display(Name = "Comment")]
    public string Content { get; set; } = "";

    /// <summary>Honeypot: real people leave this empty.</summary>
    public string? Website2 { get; set; }
}

// ---------- Account / setup ----------

public class LoginForm
{
    [Required, Display(Name = "Username or email")]
    public string Login { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class RegisterForm
{
    [Required, MaxLength(60), RegularExpression(@"^[A-Za-z0-9_.-]+$", ErrorMessage = "Letters, digits, _ . - only.")]
    public string UserName { get; set; } = "";

    [Required, EmailAddress, MaxLength(200)]
    public string Email { get; set; } = "";

    [Required, MinLength(8), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public class SetupForm
{
    [Required, MaxLength(200), Display(Name = "Site title")]
    public string SiteTitle { get; set; } = "My Mangrove Site";

    [MaxLength(300)]
    public string? Tagline { get; set; } = "Just another Mangrove site";

    [Required, MaxLength(60), RegularExpression(@"^[A-Za-z0-9_.-]+$", ErrorMessage = "Letters, digits, _ . - only."), Display(Name = "Admin username")]
    public string UserName { get; set; } = "admin";

    [Required, EmailAddress, MaxLength(200), Display(Name = "Admin email")]
    public string Email { get; set; } = "";

    [Required, MinLength(8), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";

    [Display(Name = "Add sample content")]
    public bool SampleContent { get; set; } = true;
}

public class ProfileForm
{
    [MaxLength(120), Display(Name = "Display name")]
    public string? DisplayName { get; set; }

    [Required, EmailAddress, MaxLength(200)]
    public string Email { get; set; } = "";

    [MaxLength(1000)]
    public string? Bio { get; set; }

    [DataType(DataType.Password), Display(Name = "Current password")]
    public string? CurrentPassword { get; set; }

    [MinLength(8), DataType(DataType.Password), Display(Name = "New password")]
    public string? NewPassword { get; set; }
}

// ---------- Admin ----------

public class PostEditForm
{
    public int Id { get; set; }
    public PostType Type { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = "";

    [MaxLength(200)]
    public string? Slug { get; set; }

    public string Content { get; set; } = "";

    [MaxLength(1000)]
    public string? Excerpt { get; set; }

    [MaxLength(300), Display(Name = "Meta description")]
    public string? MetaDescription { get; set; }

    public PostStatus Status { get; set; } = PostStatus.Draft;

    [Display(Name = "Publish date (UTC)")]
    public DateTime? PublishedAt { get; set; }

    [Display(Name = "Featured image")]
    public int? FeaturedMediaId { get; set; }

    [Display(Name = "Parent page")]
    public int? ParentId { get; set; }

    [Display(Name = "Order")]
    public int MenuOrder { get; set; }

    [Display(Name = "Allow comments")]
    public bool AllowComments { get; set; } = true;

    [Display(Name = "Stick to the top of the blog")]
    public bool IsSticky { get; set; }

    public List<int> CategoryIds { get; set; } = new();

    /// <summary>Comma-separated tag names.</summary>
    public string? Tags { get; set; }
}

public class UserEditForm
{
    public int Id { get; set; }

    [Required, MaxLength(60), RegularExpression(@"^[A-Za-z0-9_.-]+$", ErrorMessage = "Letters, digits, _ . - only.")]
    public string UserName { get; set; } = "";

    [Required, EmailAddress, MaxLength(200)]
    public string Email { get; set; } = "";

    [MaxLength(120), Display(Name = "Display name")]
    public string? DisplayName { get; set; }

    public UserRole Role { get; set; } = UserRole.Subscriber;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [MinLength(8), DataType(DataType.Password), Display(Name = "Password (leave blank to keep)")]
    public string? Password { get; set; }
}

public class SettingsForm
{
    [Required, MaxLength(200), Display(Name = "Site title")]
    public string SiteTitle { get; set; } = "";

    [MaxLength(300)]
    public string? Tagline { get; set; }

    [MaxLength(500), Display(Name = "Footer text")]
    public string? FooterText { get; set; }

    [Range(1, 100), Display(Name = "Posts per page")]
    public int PostsPerPage { get; set; } = 10;

    [Display(Name = "Homepage")]
    public int? HomePageId { get; set; }

    [Display(Name = "Allow comments")]
    public bool CommentsEnabled { get; set; }

    [Display(Name = "Hold new comments for approval")]
    public bool CommentModeration { get; set; }

    [Display(Name = "Anyone can register (as Subscriber)")]
    public bool AllowRegistration { get; set; }
}

public class DashboardViewModel
{
    public int Posts { get; set; }
    public int Drafts { get; set; }
    public int Pages { get; set; }
    public int PendingComments { get; set; }
    public int Users { get; set; }
    public int MediaFiles { get; set; }
    public List<Post> RecentPosts { get; set; } = new();
    public List<Comment> RecentComments { get; set; } = new();
}
