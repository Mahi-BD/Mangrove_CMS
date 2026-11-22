using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Services;

public static partial class Slug
{
    /// <summary>
    /// Lower-case, hyphen-separated slug. Latin accents are folded; letters from other
    /// scripts (Bangla, Arabic, CJK...) are kept so non-English titles still get readable URLs.
    /// </summary>
    public static string From(string? text, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark && c < 0x0370) continue; // drop Latin diacritics only
            if (char.IsLetterOrDigit(c) || cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                sb.Append(c);
            else
                sb.Append('-');
        }

        var slug = MultiDash().Replace(sb.ToString().Normalize(NormalizationForm.FormC), "-").Trim('-');
        if (slug.Length > maxLength) slug = slug[..maxLength].Trim('-');
        return slug;
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();
}

public class SlugService
{
    private readonly CmsDbContext _db;
    public SlugService(CmsDbContext db) => _db = db;

    public async Task<string> UniquePostSlugAsync(string? wanted, string fallbackTitle, PostType type, int? excludeId = null)
    {
        var baseSlug = Slug.From(string.IsNullOrWhiteSpace(wanted) ? fallbackTitle : wanted);
        if (baseSlug.Length == 0) baseSlug = type == PostType.Page ? "page" : "post";

        var slug = baseSlug;
        for (var i = 2; await _db.Posts.AnyAsync(p => p.Type == type && p.Slug == slug && p.Id != excludeId); i++)
            slug = $"{baseSlug}-{i}";
        return slug;
    }

    public async Task<string> UniqueTermSlugAsync(string? wanted, string name, TermType type, int? excludeId = null)
    {
        var baseSlug = Slug.From(string.IsNullOrWhiteSpace(wanted) ? name : wanted);
        if (baseSlug.Length == 0) baseSlug = type == TermType.Tag ? "tag" : "category";

        var slug = baseSlug;
        for (var i = 2; await _db.Terms.AnyAsync(t => t.Type == type && t.Slug == slug && t.Id != excludeId); i++)
            slug = $"{baseSlug}-{i}";
        return slug;
    }
}
