namespace Mangrove.Cms.Services;

/// <summary>Small display helpers shared by admin views and themes.</summary>
public static class Fmt
{
    /// <summary>"just now", "5 minutes ago", "yesterday", then a plain date.</summary>
    public static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalSeconds < 0) return "in " + Span(-d);
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalDays >= 7) return utc.ToString("d MMM yyyy");
        if (d.TotalDays >= 1 && d.TotalDays < 2) return "yesterday";
        return Span(d) + " ago";
    }

    private static string Span(TimeSpan d) =>
        d.TotalMinutes < 60 ? Plural((int)d.TotalMinutes, "minute") :
        d.TotalHours < 24 ? Plural((int)d.TotalHours, "hour") :
        Plural((int)d.TotalDays, "day");

    private static string Plural(int n, string unit) => $"{Math.Max(n, 1)} {unit}{(n == 1 ? "" : "s")}";

    public static string Size(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    public static string Name(Models.User? u) =>
        u == null ? "" : string.IsNullOrWhiteSpace(u.DisplayName) ? u.UserName : u.DisplayName;
}
