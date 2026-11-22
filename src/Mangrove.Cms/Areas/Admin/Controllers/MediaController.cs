using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Areas.Admin.Controllers;

public class MediaController : AdminController
{
    private readonly CmsDbContext _db;
    private readonly MediaService _media;

    public MediaController(CmsDbContext db, MediaService media)
    {
        _db = db;
        _media = media;
    }

    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var query = _db.Media.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(m => EF.Functions.Like(m.FileName, $"%{q.Trim()}%"));

        const int size = 48;
        page = Math.Max(1, page);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(m => m.UploadedAt).Skip((page - 1) * size).Take(size).ToListAsync();
        ViewBag.Q = q;
        return View(new PagedResult<Media>(items, page, size, total));
    }

    /// <summary>JSON list for the editor's media picker.</summary>
    [HttpGet]
    public async Task<IActionResult> List(bool imagesOnly = false, string? q = null)
    {
        var query = _db.Media.AsNoTracking();
        if (imagesOnly) query = query.Where(m => m.ContentType.StartsWith("image/"));
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(m => EF.Functions.Like(m.FileName, $"%{q.Trim()}%"));
        var items = await query.OrderByDescending(m => m.UploadedAt).Take(100).ToListAsync();
        return Json(items.Select(m => new { m.Id, m.FileName, m.Url, m.IsImage, m.AltText }));
    }

    [HttpPost]
    public async Task<IActionResult> Upload(List<IFormFile> files, bool json = false)
    {
        var saved = new List<Media>();
        var errors = new List<string>();
        foreach (var f in files)
        {
            var (media, error) = await _media.SaveAsync(f, CurrentUserId);
            if (media != null) saved.Add(media);
            else errors.Add($"{f.FileName}: {error}");
        }

        if (json)
            return Json(new { files = saved.Select(m => new { m.Id, m.FileName, m.Url, m.IsImage }), errors });

        if (errors.Count > 0) Flash(string.Join(" ", errors), "error");
        else Flash($"Uploaded {saved.Count} file(s).");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Update(Guid id, string? altText, string? caption)
    {
        var m = await _db.Media.FirstOrDefaultAsync(x => x.Uid == id);
        if (m == null) return NotFound();
        if (!User.AtLeast(UserRole.Editor) && m.UploadedById != CurrentUserId) return Forbid();
        m.AltText = altText?.Trim();
        m.Caption = caption?.Trim();
        await _db.SaveChangesAsync();
        Flash("Saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id)
    {
        var m = await _db.Media.FirstOrDefaultAsync(x => x.Uid == id);
        if (m == null) return NotFound();
        if (!User.AtLeast(UserRole.Editor) && m.UploadedById != CurrentUserId) return Forbid();

        _media.DeleteFile(m);
        _db.Media.Remove(m);
        await _db.SaveChangesAsync();
        Flash($"Deleted {m.FileName}.");
        return RedirectToAction(nameof(Index));
    }
}
