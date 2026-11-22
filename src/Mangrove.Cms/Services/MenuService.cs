using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Services;

/// <summary>A menu item ready to show: its resolved URL and its sub-items.</summary>
public class MenuNode
{
    public MenuItem Item { get; init; } = null!;
    public string Url { get; set; } = "/";
    public List<MenuNode> Children { get; } = new();
}

/// <summary>One row of the menu editor: a flat list where depth says how far an item is nested.</summary>
public class MenuEditItem
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
    public bool NewTab { get; set; }
    public int Depth { get; set; }
    public string LinkType { get; set; } = MenuLinkTypes.Custom;
    public int? LinkId { get; set; }
    /// <summary>Display only: the title of the linked page/category, or a note that it's gone.</summary>
    public string? Original { get; set; }
}

public class MenuSaveModel
{
    public string Location { get; set; } = "primary";
    public List<MenuEditItem> Items { get; set; } = new();
}

public class MenuService
{
    public const int MaxDepth = 2;       // 0 = top level, so three levels in all
    public const int MaxItems = 200;
    public static readonly string[] Locations = { "primary", "footer" };

    private readonly CmsDbContext _db;
    public MenuService(CmsDbContext db) => _db = db;

    private record Target(string Url, string Title, bool Live);

    /// <summary>Works out where page/post/category/tag items point today.</summary>
    private async Task<Dictionary<(string, int), Target>> ResolveAsync(IEnumerable<MenuItem> items)
    {
        var list = items.Where(i => i.LinkId.HasValue && i.LinkType != MenuLinkTypes.Custom).ToList();
        var map = new Dictionary<(string, int), Target>();
        var now = DateTime.UtcNow;

        var postIds = list.Where(i => i.LinkType is MenuLinkTypes.Page or MenuLinkTypes.Post).Select(i => i.LinkId!.Value).Distinct().ToList();
        if (postIds.Count > 0)
        {
            var posts = await _db.Posts.AsNoTracking().Where(p => postIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Type, p.Slug, p.Title, p.Status, p.PublishedAt }).ToListAsync();
            foreach (var p in posts)
            {
                var live = p.Status == PostStatus.Published || (p.Status == PostStatus.Scheduled && p.PublishedAt <= now);
                map[(p.Type == PostType.Page ? MenuLinkTypes.Page : MenuLinkTypes.Post, p.Id)] = new Target("/" + p.Slug, p.Title, live);
            }
        }

        var termIds = list.Where(i => i.LinkType is MenuLinkTypes.Category or MenuLinkTypes.Tag).Select(i => i.LinkId!.Value).Distinct().ToList();
        if (termIds.Count > 0)
        {
            var terms = await _db.Terms.AsNoTracking().Where(t => termIds.Contains(t.Id)).ToListAsync();
            foreach (var t in terms)
            {
                var kind = t.Type == TermType.Category ? MenuLinkTypes.Category : MenuLinkTypes.Tag;
                map[(kind, t.Id)] = new Target($"/{kind}/{t.Slug}", t.Name, true);
            }
        }
        return map;
    }

    private async Task<List<MenuItem>> LoadOrderedAsync(string location) =>
        await _db.MenuItems.AsNoTracking().Where(m => m.Location == location).OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync();

    /// <summary>The menu as a tree for the public site. Items whose page is gone or unpublished are left out (with their sub-items).</summary>
    public async Task<List<MenuNode>> TreeAsync(string location)
    {
        var items = await LoadOrderedAsync(location);
        var targets = await ResolveAsync(items);
        var nodes = new Dictionary<int, MenuNode>();
        foreach (var i in items)
        {
            string? url = i.Url;
            if (i.LinkType != MenuLinkTypes.Custom && i.LinkId.HasValue)
                url = targets.TryGetValue((i.LinkType, i.LinkId.Value), out var t) && t.Live ? t.Url : null;
            if (url != null) nodes[i.Id] = new MenuNode { Item = i, Url = url };
        }

        var roots = new List<MenuNode>();
        foreach (var i in items)
        {
            if (!nodes.TryGetValue(i.Id, out var node)) continue;
            if (i.ParentId is int pid)
            {
                if (nodes.TryGetValue(pid, out var parent)) parent.Children.Add(node);
                // parent hidden or missing: hide this branch too
            }
            else roots.Add(node);
        }
        return roots;
    }

    /// <summary>The menu as a flat list with depths, for the editor.</summary>
    public async Task<List<MenuEditItem>> EditListAsync(string location)
    {
        var items = await LoadOrderedAsync(location);
        var targets = await ResolveAsync(items);
        var byParent = items.ToLookup(i => i.ParentId);
        var ids = items.Select(i => i.Id).ToHashSet();
        var result = new List<MenuEditItem>();

        void Walk(int? parentId, int depth)
        {
            foreach (var i in byParent[parentId])
            {
                string? original = null;
                var url = i.Url;
                if (i.LinkType != MenuLinkTypes.Custom && i.LinkId.HasValue)
                {
                    if (targets.TryGetValue((i.LinkType, i.LinkId.Value), out var t))
                    {
                        url = t.Url;
                        original = t.Live ? t.Title : t.Title + " (not published)";
                    }
                    else original = "Deleted";
                }
                result.Add(new MenuEditItem
                {
                    Label = i.Label, Url = url, NewTab = i.OpenInNewTab, Depth = Math.Min(depth, MaxDepth),
                    LinkType = i.LinkType, LinkId = i.LinkId, Original = original,
                });
                Walk(i.Id, depth + 1);
            }
        }
        Walk(null, 0);
        // Items whose parent no longer exists: show them at the top level rather than losing them.
        foreach (var orphan in items.Where(i => i.ParentId.HasValue && !ids.Contains(i.ParentId.Value)))
            result.Add(new MenuEditItem { Label = orphan.Label, Url = orphan.Url, NewTab = orphan.OpenInNewTab, LinkType = orphan.LinkType, LinkId = orphan.LinkId });
        return result;
    }

    public static bool IsSafeUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        ((url.StartsWith('/') && !url.StartsWith("//")) || url.StartsWith('#') ||
         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase));

    /// <summary>Replaces a menu with the editor's flat list. Returns an error, or null.</summary>
    public async Task<string?> SaveAsync(MenuSaveModel model)
    {
        if (!Locations.Contains(model.Location)) return "Unknown menu.";
        if (model.Items.Count > MaxItems) return $"A menu can have at most {MaxItems} items.";
        foreach (var (it, n) in model.Items.Select((it, n) => (it, n + 1)))
        {
            if (string.IsNullOrWhiteSpace(it.Label)) return $"Item {n} needs a label.";
            if (!MenuLinkTypes.All.Contains(it.LinkType)) it.LinkType = MenuLinkTypes.Custom;
            if (it.LinkType == MenuLinkTypes.Custom && !IsSafeUrl(it.Url?.Trim()))
                return $"“{it.Label.Trim()}” needs a link starting with /, https://, http://, mailto: or tel:.";
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.MenuItems.Where(m => m.Location == model.Location).ExecuteDeleteAsync();

        // Walk the flat list keeping the last item seen at each depth; an item's parent is the
        // last item one level up. Depth can only grow by one step at a time.
        var lastAtDepth = new MenuItem?[MaxDepth + 1];
        var prevDepth = -1;
        for (var i = 0; i < model.Items.Count; i++)
        {
            var it = model.Items[i];
            var depth = Math.Clamp(it.Depth, 0, Math.Min(MaxDepth, prevDepth + 1));
            var label = it.Label.Trim();
            var url = (it.Url ?? "").Trim();
            var entity = new MenuItem
            {
                Location = model.Location,
                Label = label.Length > 120 ? label[..120] : label,
                Url = url.Length > 500 ? url[..500] : (url.Length == 0 ? "/" : url),
                OpenInNewTab = it.NewTab,
                LinkType = it.LinkType,
                LinkId = it.LinkType == MenuLinkTypes.Custom ? null : it.LinkId,
                ParentId = depth > 0 ? lastAtDepth[depth - 1]!.Id : null,
                SortOrder = i,
            };
            _db.MenuItems.Add(entity);
            await _db.SaveChangesAsync(); // need the id for children
            lastAtDepth[depth] = entity;
            prevDepth = depth;
        }
        await tx.CommitAsync();
        return null;
    }
}
