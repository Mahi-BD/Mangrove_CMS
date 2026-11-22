using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelDataReader;
using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Services;

public record ImportedSheet(List<string> Headers, List<List<string>> Rows);

/// <summary>Reading, writing and importing table-page data.</summary>
public partial class TableService
{
    public const long MaxImportBytes = 10 * 1024 * 1024;
    public const int MaxColumns = 40;
    public const int MaxRows = 20000;

    private readonly CmsDbContext _db;

    static TableService()
    {
        // Needed by ExcelDataReader for legacy .xls files and non-UTF8 CSV.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public TableService(CmsDbContext db) => _db = db;

    // ---------- Cells ----------

    public static Dictionary<string, string> ReadCells(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    // Stored as readable UTF-8 (Bangla etc. not \u-escaped). Never written into HTML unencoded.
    private static readonly JsonSerializerOptions CellJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string WriteCells(Dictionary<string, string> cells) =>
        JsonSerializer.Serialize(cells.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value), CellJson);

    public async Task<(List<TableColumn> Columns, List<TableRow> Rows)> LoadAsync(int postId)
    {
        var cols = await _db.TableColumns.AsNoTracking().Where(c => c.PostId == postId).OrderBy(c => c.SortOrder).ToListAsync();
        var rows = await _db.TableRows.AsNoTracking().Where(r => r.PostId == postId).OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToListAsync();
        return (cols, rows);
    }

    // ---------- Import ----------

    /// <summary>Reads the first sheet of an .xlsx/.xls/.csv file. The first non-empty row is the header row.</summary>
    public static (ImportedSheet? sheet, string? error) ReadSpreadsheet(IFormFile file)
    {
        if (file.Length == 0) return (null, "The file is empty.");
        if (file.Length > MaxImportBytes) return (null, "Spreadsheets must be 10 MB or smaller.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls" or ".csv"))
            return (null, "Upload an Excel file (.xlsx or .xls) or a CSV file.");

        try
        {
            using var stream = file.OpenReadStream();
            using var reader = ext == ".csv"
                ? ExcelReaderFactory.CreateCsvReader(stream)
                : ExcelReaderFactory.CreateReader(stream);

            List<string>? headers = null;
            var rows = new List<List<string>>();
            while (reader.Read())
            {
                var values = new List<string>();
                for (var i = 0; i < reader.FieldCount; i++) values.Add(CellText(reader.GetValue(i)));
                if (values.All(string.IsNullOrWhiteSpace)) continue;

                if (headers == null) { headers = values; continue; }
                rows.Add(values);
                if (rows.Count > MaxRows) return (null, $"The sheet has more than {MaxRows:N0} rows.");
            }

            if (headers == null) return (null, "The sheet is empty.");
            // Drop trailing empty header cells, and name any blank headers.
            while (headers.Count > 0 && string.IsNullOrWhiteSpace(headers[^1]) && rows.All(r => r.Count < headers.Count || string.IsNullOrWhiteSpace(r[headers.Count - 1])))
                headers.RemoveAt(headers.Count - 1);
            for (var i = 0; i < headers.Count; i++)
                if (string.IsNullOrWhiteSpace(headers[i])) headers[i] = $"Column {i + 1}";
            if (headers.Count > MaxColumns) return (null, $"The sheet has more than {MaxColumns} columns.");

            return (new ImportedSheet(headers.Select(h => h.Trim()).ToList(), rows), null);
        }
        catch (Exception ex) when (ex is ExcelDataReader.Exceptions.ExcelReaderException or InvalidDataException or IOException or ArgumentException)
        {
            return (null, "The file couldn't be read. Save it again as .xlsx or .csv and retry.");
        }
    }

    private static string CellText(object? v) => v switch
    {
        null => "",
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd") : d.ToString("yyyy-MM-dd HH:mm"),
        double n => n.ToString(n % 1 == 0 ? "0" : "0.##########", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "",
    };

    /// <summary>Guesses a column type from its header (English and Bangla) and its values.</summary>
    public static ColumnType GuessType(string header, IEnumerable<string> sample)
    {
        var h = header.Trim().ToLowerInvariant();
        if (SerialHeader().IsMatch(h)) return ColumnType.Serial;
        if (Regex.IsMatch(h, @"image|photo|picture|pic\b|avatar|ছবি")) return ColumnType.Image;
        if (Regex.IsMatch(h, @"phone|mobile|cell|contact|ph\.?\s*no|tel|মোবাইল|ফোন")) return ColumnType.Phone;
        if (Regex.IsMatch(h, @"e-?mail|ইমেইল")) return ColumnType.Email;
        if (Regex.IsMatch(h, @"\bdate\b|তারিখ")) return ColumnType.Date;
        if (Regex.IsMatch(h, @"link|url|website|web site")) return ColumnType.Link;

        var values = sample.Where(v => !string.IsNullOrWhiteSpace(v)).Take(50).ToList();
        if (values.Count > 0 && values.All(v => v.Contains('@') && v.Contains('.'))) return ColumnType.Email;
        if (values.Count > 0 && values.All(v => v.StartsWith("http://") || v.StartsWith("https://"))) return ColumnType.Link;
        return ColumnType.Text;
    }

    [GeneratedRegex(@"^(s\.?\s*l\.?|s\.?\s*n\.?|s/n|serial( no\.?)?|no\.?|#|ক্রমিক( নং)?|ক্র\.?\s*নং)$")]
    private static partial Regex SerialHeader();

    /// <summary>Replaces (or appends to) a table with the contents of a spreadsheet.</summary>
    public async Task<(int columns, int rows)> ImportAsync(int postId, ImportedSheet sheet, bool replace)
    {
        var columns = await _db.TableColumns.Where(c => c.PostId == postId).OrderBy(c => c.SortOrder).ToListAsync();

        if (replace)
        {
            await _db.TableRows.Where(r => r.PostId == postId).ExecuteDeleteAsync();
            _db.TableColumns.RemoveRange(columns);
            await _db.SaveChangesAsync();
            columns = new();
        }

        // Match spreadsheet headers to existing columns by name; add any new ones.
        var map = new List<TableColumn?>();
        for (var i = 0; i < sheet.Headers.Count; i++)
        {
            var header = sheet.Headers[i];
            var col = columns.FirstOrDefault(c => string.Equals(c.Name, header, StringComparison.OrdinalIgnoreCase));
            if (col == null)
            {
                if (columns.Count >= MaxColumns) { map.Add(null); continue; }
                var idx = i;
                col = new TableColumn
                {
                    PostId = postId,
                    Name = header.Length > 120 ? header[..120] : header,
                    Type = GuessType(header, sheet.Rows.Select(r => idx < r.Count ? r[idx] : "")),
                    SortOrder = columns.Count,
                };
                _db.TableColumns.Add(col);
                columns.Add(col);
            }
            map.Add(col);
        }
        await _db.SaveChangesAsync();

        var start = await _db.TableRows.Where(r => r.PostId == postId).MaxAsync(r => (int?)r.SortOrder) ?? -1;
        var added = 0;
        foreach (var values in sheet.Rows)
        {
            var cells = new Dictionary<string, string>();
            for (var i = 0; i < map.Count && i < values.Count; i++)
            {
                var col = map[i];
                if (col == null || col.Type == ColumnType.Serial) continue;
                cells[col.Id.ToString()] = values[i];
            }
            _db.TableRows.Add(new TableRow { PostId = postId, SortOrder = start + 1 + added, Data = WriteCells(cells) });
            added++;
        }
        await _db.SaveChangesAsync();
        return (columns.Count, added);
    }

    // ---------- Display helpers ----------

    /// <summary>Short label for the column-type picker.</summary>
    public static string ShortType(ColumnType t) => t switch
    {
        ColumnType.Serial => "Serial no.",
        ColumnType.Phone => "Phone",
        _ => t.ToString(),
    };

    public static string DisplayType(ColumnType t) => t switch
    {
        ColumnType.Serial => "Serial no. (automatic)",
        ColumnType.Text => "Text",
        ColumnType.Number => "Number",
        ColumnType.Phone => "Phone number",
        ColumnType.Email => "Email",
        ColumnType.Image => "Image",
        ColumnType.Link => "Link",
        ColumnType.Date => "Date",
        _ => t.ToString(),
    };

    /// <summary>Only http(s) and site-relative URLs are rendered as links or images.</summary>
    public static bool IsSafeUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        ((url.StartsWith('/') && !url.StartsWith("//")) ||
         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    public static string TelHref(string phone) => "tel:" + Regex.Replace(phone, @"[^\d+]", "");
}
