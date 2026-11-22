using System.Globalization;
using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Services;

/// <summary>Saves a notice board's notices as edited in the page editor.</summary>
public class NoticeService
{
    public const int MaxNotices = 5000;
    private readonly CmsDbContext _db;
    public NoticeService(CmsDbContext db) => _db = db;

    /// <summary>Makes the board's notices match the list: updates, adds and deletes. Returns an error or null.</summary>
    public async Task<string?> SyncAsync(int postId, List<NoticeSaveModel> list, int? userId)
    {
        if (list.Count > MaxNotices) return $"A notice board can hold at most {MaxNotices:N0} notices.";
        if (list.Any(n => string.IsNullOrWhiteSpace(n.Title))) return "Every notice needs a title.";

        var existing = await _db.Notices.Include(n => n.Attachments).Where(n => n.PostId == postId).ToDictionaryAsync(n => n.Id);
        var mediaIds = list.SelectMany(n => n.Files.Select(f => f.MediaId)).Distinct().ToList();
        var validMedia = (await _db.Media.Where(m => mediaIds.Contains(m.Id)).Select(m => m.Id).ToListAsync()).ToHashSet();
        var now = DateTime.UtcNow;

        foreach (var item in list)
        {
            if (item.Id == 0 || !existing.Remove(item.Id, out var notice))
            {
                notice = new Notice { PostId = postId, CreatedById = userId, CreatedAt = now };
                _db.Notices.Add(notice);
            }
            var title = item.Title.Trim();
            notice.Title = title.Length > 300 ? title[..300] : title;
            notice.NoticeDate = DateTime.TryParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d : now.Date;
            notice.Description = item.Description ?? "";
            notice.IsPinned = item.Pinned;
            notice.IsPublished = item.Published;
            notice.UpdatedAt = now;

            var wanted = item.Files.Select(f => f.MediaId).Where(validMedia.Contains).Distinct().ToList();
            notice.Attachments.RemoveAll(a => !wanted.Contains(a.MediaId));
            for (var i = 0; i < wanted.Count; i++)
            {
                var att = notice.Attachments.FirstOrDefault(a => a.MediaId == wanted[i]);
                if (att == null) notice.Attachments.Add(new NoticeAttachment { MediaId = wanted[i], SortOrder = i });
                else att.SortOrder = i;
            }
        }

        _db.Notices.RemoveRange(existing.Values); // notices removed in the editor
        await _db.SaveChangesAsync();
        return null;
    }
}
