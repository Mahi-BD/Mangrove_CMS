using Mangrove.Cms.Models;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Data;

public class CmsDbContext : DbContext
{
    public CmsDbContext(DbContextOptions<CmsDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<PostTerm> PostTerms => Set<PostTerm>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<TableColumn> TableColumns => Set<TableColumn>();
    public DbSet<TableRow> TableRows => Set<TableRow>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<NoticeAttachment> NoticeAttachments => Set<NoticeAttachment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            // Case-insensitive lookups for login.
            e.Property(x => x.UserName).UseCollation("NOCASE");
            e.Property(x => x.Email).UseCollation("NOCASE");
        });

        b.Entity<Post>(e =>
        {
            e.HasIndex(x => new { x.Type, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.Type, x.Status, x.PublishedAt });
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FeaturedMedia).WithMany().HasForeignKey(x => x.FeaturedMediaId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Term>(e =>
        {
            e.HasIndex(x => new { x.Type, x.Slug }).IsUnique();
        });

        b.Entity<PostTerm>(e =>
        {
            e.HasKey(x => new { x.PostId, x.TermId });
            e.HasOne(x => x.Post).WithMany(p => p.PostTerms).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Term).WithMany(t => t.PostTerms).HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Comment>(e =>
        {
            e.HasIndex(x => new { x.PostId, x.Status });
            e.HasOne(x => x.Post).WithMany(p => p.Comments).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MenuItem>(e => e.HasIndex(x => new { x.Location, x.SortOrder }));

        // Table pages and notice boards belong to a page and go when it is deleted.
        b.Entity<TableColumn>(e =>
        {
            e.HasIndex(x => new { x.PostId, x.SortOrder });
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TableRow>(e =>
        {
            e.HasIndex(x => new { x.PostId, x.SortOrder });
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Notice>(e =>
        {
            e.HasIndex(x => new { x.PostId, x.IsPinned, x.NoticeDate });
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        // Every record that appears in a URL has a unique random Uid.
        b.Entity<User>().HasIndex(x => x.Uid).IsUnique();
        b.Entity<Post>().HasIndex(x => x.Uid).IsUnique();
        b.Entity<Term>().HasIndex(x => x.Uid).IsUnique();
        b.Entity<Comment>().HasIndex(x => x.Uid).IsUnique();
        b.Entity<Media>().HasIndex(x => x.Uid).IsUnique();
        b.Entity<Notice>().HasIndex(x => x.Uid).IsUnique();

        b.Entity<NoticeAttachment>(e =>
        {
            e.HasOne(x => x.Notice).WithMany(n => n.Attachments).HasForeignKey(x => x.NoticeId).OnDelete(DeleteBehavior.Cascade);
            // Deleting a file from the media library removes it from notices too.
            e.HasOne(x => x.Media).WithMany().HasForeignKey(x => x.MediaId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
