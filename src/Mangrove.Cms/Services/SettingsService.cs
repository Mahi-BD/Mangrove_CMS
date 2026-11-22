using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mangrove.Cms.Services;

/// <summary>Well-known setting keys and their defaults.</summary>
public static class SettingKeys
{
    public const string SiteTitle = "site.title";
    public const string Tagline = "site.tagline";
    public const string FooterText = "site.footer";
    public const string Theme = "site.theme";
    public const string PostsPerPage = "reading.postsPerPage";
    public const string HomePageId = "reading.homePageId";      // empty = latest posts
    public const string CommentsEnabled = "discussion.enabled";
    public const string CommentModeration = "discussion.moderation";
    public const string AllowRegistration = "users.allowRegistration";
    public const string Installed = "system.installed";

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [SiteTitle] = "Mangrove CMS",
        [Tagline] = "Just another Mangrove site",
        [FooterText] = "Powered by Mangrove CMS",
        [Theme] = "Mangrove",
        [PostsPerPage] = "10",
        [HomePageId] = "",
        [CommentsEnabled] = "true",
        [CommentModeration] = "true",
        [AllowRegistration] = "false",
        [Installed] = "false",
    };
}

public class SettingsService
{
    private const string CacheKey = "mangrove.settings";
    private readonly CmsDbContext _db;
    private readonly IMemoryCache _cache;

    public SettingsService(CmsDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyDictionary<string, string>> AllAsync()
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, string>? cached) && cached != null)
            return cached;

        var stored = await _db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value ?? "");
        var merged = new Dictionary<string, string>(SettingKeys.Defaults);
        foreach (var kv in stored) merged[kv.Key] = kv.Value;

        _cache.Set(CacheKey, (IReadOnlyDictionary<string, string>)merged, TimeSpan.FromMinutes(10));
        return merged;
    }

    public async Task<string> GetAsync(string key)
    {
        var all = await AllAsync();
        return all.TryGetValue(key, out var v) ? v : "";
    }

    public async Task<bool> GetBoolAsync(string key) =>
        bool.TryParse(await GetAsync(key), out var b) && b;

    public async Task<int> GetIntAsync(string key, int fallback) =>
        int.TryParse(await GetAsync(key), out var i) ? i : fallback;

    public async Task SetManyAsync(IDictionary<string, string?> values)
    {
        var keys = values.Keys.ToList();
        var existing = await _db.Settings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key);
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var row)) row.Value = value;
            else _db.Settings.Add(new Setting { Key = key, Value = value });
        }
        await _db.SaveChangesAsync();
        _cache.Remove(CacheKey);
    }

    public Task SetAsync(string key, string? value) =>
        SetManyAsync(new Dictionary<string, string?> { [key] = value });
}
