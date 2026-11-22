using System.ComponentModel.DataAnnotations;

namespace Mangrove.Cms.Models;

public enum UserRole
{
    Subscriber = 0,
    Author = 1,
    Editor = 2,
    Administrator = 3
}

public enum PostType
{
    Post = 0,
    Page = 1
}

public enum PostStatus
{
    Draft = 0,
    Published = 1,
    Scheduled = 2,
    Trash = 3
}

public enum TermType
{
    Category = 0,
    Tag = 1
}

public enum CommentStatus
{
    Pending = 0,
    Approved = 1,
    Spam = 2,
    Trash = 3
}

public class User
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string UserName { get; set; } = "";

    [Required, MaxLength(200), EmailAddress]
    public string Email { get; set; } = "";

    [MaxLength(120)]
    public string DisplayName { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public UserRole Role { get; set; } = UserRole.Subscriber;

    [MaxLength(1000)]
    public string? Bio { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Changes whenever the password or role changes; stale cookies are rejected.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public class Post
{
    public int Id { get; set; }

    public PostType Type { get; set; } = PostType.Post;
    public PostStatus Status { get; set; } = PostStatus.Draft;

    [Required, MaxLength(300)]
    public string Title { get; set; } = "";

    [Required, MaxLength(200)]
    public string Slug { get; set; } = "";

    /// <summary>Body in Markdown (raw HTML allowed; sanitized on render).</summary>
    public string Content { get; set; } = "";

    [MaxLength(1000)]
    public string? Excerpt { get; set; }

    [MaxLength(300)]
    public string? MetaDescription { get; set; }

    public int AuthorId { get; set; }
    public User? Author { get; set; }

    public int? FeaturedMediaId { get; set; }
    public Media? FeaturedMedia { get; set; }

    /// <summary>Pages only: optional parent page for nested URLs and menus.</summary>
    public int? ParentId { get; set; }

    public int MenuOrder { get; set; }
    public bool AllowComments { get; set; } = true;
    public bool IsSticky { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }

    public List<PostTerm> PostTerms { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();

    public bool IsLive(DateTime utcNow) =>
        Status == PostStatus.Published ||
        (Status == PostStatus.Scheduled && PublishedAt.HasValue && PublishedAt.Value <= utcNow);
}

public class Term
{
    public int Id { get; set; }
    public TermType Type { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = "";

    [Required, MaxLength(140)]
    public string Slug { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? ParentId { get; set; }

    public List<PostTerm> PostTerms { get; set; } = new();
}

public class PostTerm
{
    public int PostId { get; set; }
    public Post? Post { get; set; }
    public int TermId { get; set; }
    public Term? Term { get; set; }
}

public class Comment
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public Post? Post { get; set; }

    public int? ParentId { get; set; }
    public int? UserId { get; set; }

    [Required, MaxLength(120)]
    public string AuthorName { get; set; } = "";

    [MaxLength(200)]
    public string? AuthorEmail { get; set; }

    [MaxLength(300)]
    public string? AuthorUrl { get; set; }

    [Required, MaxLength(5000)]
    public string Content { get; set; } = "";

    public CommentStatus Status { get; set; } = CommentStatus.Pending;

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Media
{
    public int Id { get; set; }

    [Required, MaxLength(260)]
    public string FileName { get; set; } = "";

    /// <summary>Path under wwwroot, e.g. "uploads/2026/11/abc.jpg".</summary>
    [Required, MaxLength(400)]
    public string StoredPath { get; set; } = "";

    [MaxLength(120)]
    public string ContentType { get; set; } = "";

    public long SizeBytes { get; set; }

    [MaxLength(300)]
    public string? AltText { get; set; }

    [MaxLength(500)]
    public string? Caption { get; set; }

    public int? UploadedById { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public string Url => "/" + StoredPath.TrimStart('/');
}

public class MenuItem
{
    public int Id { get; set; }

    /// <summary>Theme location, e.g. "primary" or "footer".</summary>
    [Required, MaxLength(40)]
    public string Location { get; set; } = "primary";

    [Required, MaxLength(120)]
    public string Label { get; set; } = "";

    [Required, MaxLength(500)]
    public string Url { get; set; } = "/";

    public int? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool OpenInNewTab { get; set; }
}

public class Setting
{
    [Key, MaxLength(100)]
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}
