using System.ComponentModel.DataAnnotations;
using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Areas.Admin.Controllers;

// ======================================================================
// Table pages
// ======================================================================

public class TableSaveColumn
{
    /// <summary>Existing column id as a string, or a temporary client key like "n3" for a new column.</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public ColumnType Type { get; set; }
    public int? Width { get; set; }
}

public class TableSaveRow
{
    public Dictionary<string, string> Cells { get; set; } = new();
}

public class TableSaveModel
{
    public int PostId { get; set; }
    public List<TableSaveColumn> Columns { get; set; } = new();
    public List<TableSaveRow> Rows { get; set; } = new();
    public int PageSize { get; set; } = 25;
    public bool Searchable { get; set; } = true;
}

[Authorize(Policy = Policies.Editor)]
public class PageTableController : AdminController
{
    private readonly CmsDbContext _db;
    private readonly TableService _tables;

    public PageTableController(CmsDbContext db, TableService tables)
    {
        _db = db;
        _tables = tables;
    }

    private Task<Post?> FindPage(int id) =>
        _db.Posts.FirstOrDefaultAsync(p => p.Id == id && p.Type == PostType.Page && p.Kind == PageKind.Table);

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var page = await FindPage(id);
        if (page == null) return NotFound();
        var (cols, rows) = await _tables.LoadAsync(id);
        ViewBag.Columns = cols;
        ViewBag.Rows = rows;
        return View(page);
    }

    /// <summary>Saves the whole table (columns, rows, display settings) in one go from the grid editor.</summary>
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] TableSaveModel model)
    {
        var page = await FindPage(model.PostId);
        if (page == null) return NotFound();

        if (model.Columns.Count > TableService.MaxColumns)
            return BadRequest(new { error = $"A table can have at most {TableService.MaxColumns} columns." });
        if (model.Rows.Count > TableService.MaxRows)
            return BadRequest(new { error = $"A table can have at most {TableService.MaxRows:N0} rows." });
        if (model.Columns.Any(c => string.IsNullOrWhiteSpace(c.Name)))
            return BadRequest(new { error = "Every column needs a name." });

        await using var tx = await _db.Database.BeginTransactionAsync();

        var existing = await _db.TableColumns.Where(c => c.PostId == page.Id).ToDictionaryAsync(c => c.Id.ToString());
        var keyToId = new Dictionary<string, string>();
        var kept = new HashSet<string>();
        for (var i = 0; i < model.Columns.Count; i++)
        {
            var c = model.Columns[i];
            if (!existing.TryGetValue(c.Key, out var col))
            {
                col = new TableColumn { PostId = page.Id };
                _db.TableColumns.Add(col);
            }
            col.Name = c.Name.Trim().Length > 120 ? c.Name.Trim()[..120] : c.Name.Trim();
            col.Type = Enum.IsDefined(c.Type) ? c.Type : ColumnType.Text;
            col.Width = c.Width is >= 90 and <= 1200 ? c.Width : null;
            col.SortOrder = i;
            kept.Add(c.Key);
        }
        _db.TableColumns.RemoveRange(existing.Where(kv => !kept.Contains(kv.Key)).Select(kv => kv.Value));
        await _db.SaveChangesAsync();

        // Now every column has a real id: map client keys to it.
        var saved = await _db.TableColumns.Where(c => c.PostId == page.Id).OrderBy(c => c.SortOrder).ToListAsync();
        for (var i = 0; i < model.Columns.Count; i++) keyToId[model.Columns[i].Key] = saved[i].Id.ToString();
        var serialIds = saved.Where(c => c.Type == ColumnType.Serial).Select(c => c.Id.ToString()).ToHashSet();

        await _db.TableRows.Where(r => r.PostId == page.Id).ExecuteDeleteAsync();
        var order = 0;
        foreach (var row in model.Rows)
        {
            var cells = new Dictionary<string, string>();
            foreach (var (key, value) in row.Cells)
            {
                if (!keyToId.TryGetValue(key, out var colId) || serialIds.Contains(colId)) continue;
                var v = (value ?? "").Trim();
                if (v.Length > 4000) v = v[..4000];
                cells[colId] = v;
            }
            if (cells.Count == 0) continue; // skip completely empty rows
            _db.TableRows.Add(new TableRow { PostId = page.Id, SortOrder = order++, Data = TableService.WriteCells(cells) });
        }

        page.ListPageSize = Math.Clamp(model.PageSize, 0, 500);
        page.ListSearchable = model.Searchable;
        page.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Json(new { ok = true, rows = order, columns = saved.Select(c => new { key = c.Id.ToString(), c.Name, c.Type, c.Width }) });
    }

    [HttpPost]
    public async Task<IActionResult> Import(int id, IFormFile? file, bool replace = false)
    {
        var page = await FindPage(id);
        if (page == null) return NotFound();
        if (file == null)
        {
            Flash("Choose an Excel or CSV file to import.", "error");
            return RedirectToAction(nameof(Edit), new { id });
        }

        var (sheet, error) = TableService.ReadSpreadsheet(file);
        if (sheet == null)
        {
            Flash(error ?? "The file couldn't be read.", "error");
            return RedirectToAction(nameof(Edit), new { id });
        }

        var (cols, rows) = await _tables.ImportAsync(id, sheet, replace);
        page.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        Flash(replace
            ? $"Imported {rows:N0} rows into {cols} columns. Check the column types, then save."
            : $"Added {rows:N0} rows from {file.FileName}.");
        return RedirectToAction(nameof(Edit), new { id });
    }
}

// ======================================================================
// Notice boards
// ======================================================================

public class NoticeEditForm
{
    public int Id { get; set; }
    public int PostId { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = "";

    [Display(Name = "Notice date")]
    [DataType(DataType.Date)]
    public DateTime NoticeDate { get; set; } = DateTime.UtcNow.Date;

    public string? Description { get; set; }

    [Display(Name = "Pin to the top")]
    public bool IsPinned { get; set; }

    [Display(Name = "Visible on the site")]
    public bool IsPublished { get; set; } = true;

    /// <summary>Existing attachments to keep (unchecked ones are removed).</summary>
    public List<int> KeepAttachmentIds { get; set; } = new();

    /// <summary>Files chosen from the media library, comma-separated media ids.</summary>
    public string? LibraryMediaIds { get; set; }
}

[Authorize(Policy = Policies.Editor)]
public class NoticesController : AdminController
{
    private readonly CmsDbContext _db;
    private readonly MediaService _media;

    public NoticesController(CmsDbContext db, MediaService media)
    {
        _db = db;
        _media = media;
    }

    private Task<Post?> FindBoard(int id) =>
        _db.Posts.FirstOrDefaultAsync(p => p.Id == id && p.Type == PostType.Page && p.Kind == PageKind.Notices);

    /// <summary>All notices on one board page. Route id = the page id.</summary>
    public async Task<IActionResult> Index(int id, string? q, int page = 1)
    {
        var board = await FindBoard(id);
        if (board == null) return NotFound();

        var query = _db.Notices.AsNoTracking().Where(n => n.PostId == id);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(n => EF.Functions.Like(n.Title, $"%{q.Trim()}%"));

        const int size = 30;
        page = Math.Max(1, page);
        var total = await query.CountAsync();
        var items = await query.Include(n => n.Attachments)
            .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.NoticeDate).ThenByDescending(n => n.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync();

        ViewBag.Board = board;
        ViewBag.Q = q;
        return View(new PagedResult<Notice>(items, page, size, total));
    }

    [HttpGet]
    public async Task<IActionResult> Create(int id)
    {
        var board = await FindBoard(id);
        if (board == null) return NotFound();
        ViewBag.Board = board;
        ViewBag.Attachments = new List<NoticeAttachment>();
        return View("Edit", new NoticeEditForm { PostId = id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var n = await _db.Notices.Include(x => x.Attachments).ThenInclude(a => a.Media).FirstOrDefaultAsync(x => x.Id == id);
        if (n == null) return NotFound();
        ViewBag.Board = await FindBoard(n.PostId);
        ViewBag.Attachments = n.Attachments.OrderBy(a => a.SortOrder).ToList();
        return View(new NoticeEditForm
        {
            Id = n.Id, PostId = n.PostId, Title = n.Title, NoticeDate = n.NoticeDate, Description = n.Description,
            IsPinned = n.IsPinned, IsPublished = n.IsPublished,
            KeepAttachmentIds = n.Attachments.Select(a => a.Id).ToList(),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Save(NoticeEditForm form, List<IFormFile> files)
    {
        var board = await FindBoard(form.PostId);
        if (board == null) return NotFound();

        Notice? notice = null;
        if (form.Id != 0)
        {
            notice = await _db.Notices.Include(n => n.Attachments).FirstOrDefaultAsync(n => n.Id == form.Id && n.PostId == board.Id);
            if (notice == null) return NotFound();
        }

        // Upload new files first so a validation error doesn't lose track of them.
        var uploaded = new List<Media>();
        var errors = new List<string>();
        foreach (var f in files.Where(f => f.Length > 0))
        {
            var (m, err) = await _media.SaveAsync(f, CurrentUserId);
            if (m != null) uploaded.Add(m); else errors.Add($"{f.FileName}: {err}");
        }
        foreach (var e in errors) ModelState.AddModelError("", e);

        if (!ModelState.IsValid)
        {
            ViewBag.Board = board;
            ViewBag.Attachments = notice == null ? new List<NoticeAttachment>()
                : await _db.NoticeAttachments.Include(a => a.Media).Where(a => a.NoticeId == notice.Id).OrderBy(a => a.SortOrder).ToListAsync();
            if (uploaded.Count > 0)
                form.LibraryMediaIds = string.Join(",", new[] { form.LibraryMediaIds }.Concat(uploaded.Select(u => u.Id.ToString())).Where(s => !string.IsNullOrEmpty(s)));
            return View("Edit", form);
        }

        var now = DateTime.UtcNow;
        if (notice == null)
        {
            notice = new Notice { PostId = board.Id, CreatedById = CurrentUserId, CreatedAt = now };
            _db.Notices.Add(notice);
        }
        notice.Title = form.Title.Trim();
        notice.NoticeDate = form.NoticeDate.Date;
        notice.Description = form.Description ?? "";
        notice.IsPinned = form.IsPinned;
        notice.IsPublished = form.IsPublished;
        notice.UpdatedAt = now;

        notice.Attachments.RemoveAll(a => !form.KeepAttachmentIds.Contains(a.Id));

        var libraryIds = (form.LibraryMediaIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0).ToList();
        var validLibrary = await _db.Media.Where(m => libraryIds.Contains(m.Id)).Select(m => m.Id).ToListAsync();
        var order = notice.Attachments.Count == 0 ? 0 : notice.Attachments.Max(a => a.SortOrder) + 1;
        foreach (var mediaId in validLibrary.Concat(uploaded.Select(u => u.Id)).Distinct())
        {
            if (notice.Attachments.Any(a => a.MediaId == mediaId)) continue;
            notice.Attachments.Add(new NoticeAttachment { MediaId = mediaId, SortOrder = order++ });
        }

        board.UpdatedAt = now;
        await _db.SaveChangesAsync();
        Flash(form.Id == 0 ? $"Notice “{notice.Title}” added." : "Notice saved.");
        return RedirectToAction(nameof(Index), new { id = board.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var n = await _db.Notices.FindAsync(id);
        if (n == null) return NotFound();
        _db.Notices.Remove(n);
        await _db.SaveChangesAsync();
        Flash($"Deleted “{n.Title}”. Its files stay in the media library.");
        return RedirectToAction(nameof(Index), new { id = n.PostId });
    }
}
