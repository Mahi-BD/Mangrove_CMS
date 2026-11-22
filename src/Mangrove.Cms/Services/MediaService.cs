using Mangrove.Cms.Data;
using Mangrove.Cms.Models;

namespace Mangrove.Cms.Services;

public class MediaService
{
    public const long MaxBytes = 25 * 1024 * 1024;

    // Extension → content type. Anything not listed is rejected (no SVG/HTML: script risk).
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".avif"] = "image/avif",
        [".ico"] = "image/x-icon",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".csv"] = "text/csv",
        [".zip"] = "application/zip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".mp3"] = "audio/mpeg", [".ogg"] = "audio/ogg", [".wav"] = "audio/wav",
        [".mp4"] = "video/mp4", [".webm"] = "video/webm",
    };

    public static string AcceptList => string.Join(",", Allowed.Keys);

    private readonly CmsDbContext _db;
    private readonly IWebHostEnvironment _env;

    public MediaService(CmsDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<(Media? media, string? error)> SaveAsync(IFormFile file, int? userId)
    {
        if (file.Length == 0) return (null, "The file is empty.");
        if (file.Length > MaxBytes) return (null, $"Files must be {MaxBytes / 1024 / 1024} MB or smaller.");

        var ext = Path.GetExtension(file.FileName);
        if (!Allowed.TryGetValue(ext, out var contentType))
            return (null, $"File type '{ext}' is not allowed.");

        var now = DateTime.UtcNow;
        var relDir = Path.Combine("uploads", now.ToString("yyyy"), now.ToString("MM"));
        var absDir = Path.Combine(_env.WebRootPath, relDir);
        Directory.CreateDirectory(absDir);

        var baseName = Slug.From(Path.GetFileNameWithoutExtension(file.FileName), 60);
        if (baseName.Length == 0) baseName = "file";
        var storedName = $"{baseName}-{Guid.NewGuid().ToString("N")[..8]}{ext.ToLowerInvariant()}";

        await using (var stream = File.Create(Path.Combine(absDir, storedName)))
            await file.CopyToAsync(stream);

        var media = new Media
        {
            FileName = Path.GetFileName(file.FileName),
            StoredPath = Path.Combine(relDir, storedName).Replace('\\', '/'),
            ContentType = contentType,
            SizeBytes = file.Length,
            UploadedById = userId,
            UploadedAt = now,
        };
        _db.Media.Add(media);
        await _db.SaveChangesAsync();
        return (media, null);
    }

    public void DeleteFile(Media media)
    {
        var abs = Path.GetFullPath(Path.Combine(_env.WebRootPath, media.StoredPath));
        var uploadsRoot = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads")) + Path.DirectorySeparatorChar;
        if (abs.StartsWith(uploadsRoot, StringComparison.Ordinal) && File.Exists(abs))
            File.Delete(abs);
    }
}
