using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Services;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

/// <summary>Read-side queries for the public site. Only live (published or due) content is returned.</summary>
public class PostQueryService
{
    private readonly CmsDbContext _db;
    public PostQueryService(CmsDbContext db) => _db = db;

    public IQueryable<Post> Live(PostType type)
    {
        var now = DateTime.UtcNow;
        return _db.Posts.AsNoTracking()
            .Where(p => p.Type == type)
            .Where(p => p.Status == PostStatus.Published ||
                        (p.Status == PostStatus.Scheduled && p.PublishedAt != null && p.PublishedAt <= now));
    }

    public async Task<PagedResult<Post>> PageAsync(IQueryable<Post> query, int page, int pageSize, bool stickyFirst = false)
    {
        page = Math.Max(1, page);
        var total = await query.CountAsync();
        var ordered = stickyFirst
            ? query.OrderByDescending(p => p.IsSticky).ThenByDescending(p => p.PublishedAt)
            : query.OrderByDescending(p => p.PublishedAt);

        var items = await ordered
            .Include(p => p.Author)
            .Include(p => p.FeaturedMedia)
            .Include(p => p.PostTerms).ThenInclude(pt => pt.Term)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsSplitQuery()
            .ToListAsync();
        return new PagedResult<Post>(items, page, pageSize, total);
    }

    public IQueryable<Post> Search(string q)
    {
        var like = $"%{q.Trim()}%";
        return Live(PostType.Post).Where(p => EF.Functions.Like(p.Title, like) || EF.Functions.Like(p.Content, like));
    }

    public async Task<List<Term>> TermsWithCountsAsync(TermType type) =>
        await _db.Terms.AsNoTracking().Where(t => t.Type == type).OrderBy(t => t.Name).ToListAsync();

    public async Task<List<Post>> RecentAsync(int count) =>
        await Live(PostType.Post).OrderByDescending(p => p.PublishedAt).Take(count).ToListAsync();
}
