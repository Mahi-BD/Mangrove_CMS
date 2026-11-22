using System.ComponentModel.DataAnnotations;

namespace Mangrove.Cms.Models;

/// <summary>What a page shows besides its body text.</summary>
public enum PageKind
{
    Standard = 0,
    Table = 1,
    Notices = 2
}

public enum ColumnType
{
    Text = 0,
    Serial = 1,   // automatic 1, 2, 3… numbering; no stored value
    Number = 2,
    Phone = 3,
    Email = 4,
    Image = 5,    // stores an image URL (usually from the media library)
    Link = 6,
    Date = 7
}

/// <summary>A column of a table page.</summary>
public class TableColumn
{
    public int Id { get; set; }
    public int PostId { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = "";

    public ColumnType Type { get; set; } = ColumnType.Text;

    /// <summary>Width in pixels chosen by dragging the column edge; null = automatic.</summary>
    public int? Width { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>A row of a table page. Cell values are stored as JSON: { "columnId": "value" }.</summary>
public class TableRow
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public int SortOrder { get; set; }
    public string Data { get; set; } = "{}";
}

/// <summary>An entry on a notice-board page.</summary>
public class Notice
{
    public int Id { get; set; }

    /// <summary>Public identifier used in URLs and forms, so records can't be guessed by counting.</summary>
    public Guid Uid { get; set; } = Guid.NewGuid();
    public int PostId { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = "";

    /// <summary>Markdown, shown on the notice's detail page.</summary>
    public string Description { get; set; } = "";

    /// <summary>The date shown on the notice (the issue date), stored as a date.</summary>
    public DateTime NoticeDate { get; set; } = DateTime.UtcNow.Date;

    public bool IsPinned { get; set; }
    public bool IsPublished { get; set; } = true;

    public int? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<NoticeAttachment> Attachments { get; set; } = new();
}

/// <summary>A file attached to a notice. Files live in the media library.</summary>
public class NoticeAttachment
{
    public int Id { get; set; }
    public int NoticeId { get; set; }
    public Notice? Notice { get; set; }
    public int MediaId { get; set; }
    public Media? Media { get; set; }
    public int SortOrder { get; set; }
}

// ---------- What the page editor sends for table pages and notice boards ----------

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
    public List<TableSaveColumn> Columns { get; set; } = new();
    public List<TableSaveRow> Rows { get; set; } = new();
}

public class NoticeSaveFile
{
    public int MediaId { get; set; }
}

public class NoticeSaveModel
{
    /// <summary>0 for a notice added in this edit.</summary>
    public int Id { get; set; }
    public string Title { get; set; } = "";
    /// <summary>yyyy-MM-dd</summary>
    public string? Date { get; set; }
    public string? Description { get; set; }
    public bool Pinned { get; set; }
    public bool Published { get; set; } = true;
    public List<NoticeSaveFile> Files { get; set; } = new();
}
