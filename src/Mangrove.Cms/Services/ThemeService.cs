using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Razor;

namespace Mangrove.Cms.Services;

public record ThemeInfo(string Folder, string Name, string Description, string Author, string Version);

/// <summary>
/// Themes live in /Themes/{Folder}/ with a theme.json and Razor views
/// (Index, Single, Archive, Search, _Layout, ...). Static assets go in wwwroot/themes/{folder}/.
/// </summary>
public class ThemeService
{
    public const string HttpContextKey = "mangrove.theme";
    private readonly IWebHostEnvironment _env;

    public ThemeService(IWebHostEnvironment env) => _env = env;

    public IReadOnlyList<ThemeInfo> List()
    {
        var root = Path.Combine(_env.ContentRootPath, "Themes");
        if (!Directory.Exists(root)) return Array.Empty<ThemeInfo>();

        var list = new List<ThemeInfo>();
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d))
        {
            var folder = Path.GetFileName(dir);
            var manifest = Path.Combine(dir, "theme.json");
            string name = folder, desc = "", author = "", version = "";
            if (File.Exists(manifest))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                    var r = doc.RootElement;
                    name = r.TryGetProperty("name", out var n) ? n.GetString() ?? folder : folder;
                    desc = r.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    author = r.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "";
                    version = r.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                }
                catch (JsonException) { /* broken manifest: still list the folder */ }
            }
            list.Add(new ThemeInfo(folder, name, desc, author, version));
        }
        return list;
    }

    public bool Exists(string folder) =>
        !string.IsNullOrWhiteSpace(folder) &&
        folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        Directory.Exists(Path.Combine(_env.ContentRootPath, "Themes", folder));
}

/// <summary>Looks for public views in the active theme first, then the default theme.</summary>
public class ThemeViewLocationExpander : IViewLocationExpander
{
    public void PopulateValues(ViewLocationExpanderContext context)
    {
        if (context.ActionContext.HttpContext.Items.TryGetValue(ThemeService.HttpContextKey, out var t) && t is string theme)
            context.Values["theme"] = theme;
    }

    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
    {
        if (context.AreaName != null) return viewLocations; // admin area is not themed

        var theme = context.Values.TryGetValue("theme", out var t) ? t : null;
        var themes = new List<string>();
        if (!string.IsNullOrEmpty(theme) && theme != "Mangrove") themes.Add(theme);
        themes.Add("Mangrove");

        // Order: theme/Controller/View → Views/Controller/View → theme/View → Views/Shared/View.
        // So /Views/Setup/Index wins over the theme's (blog) Index, but themes can still
        // override any controller view by adding Themes/<Name>/<Controller>/<View>.cshtml.
        var result = new List<string>();
        result.AddRange(themes.Select(x => $"/Themes/{x}/{{1}}/{{0}}.cshtml"));
        result.Add("/Views/{1}/{0}.cshtml");
        result.AddRange(themes.Select(x => $"/Themes/{x}/{{0}}.cshtml"));
        result.AddRange(viewLocations.Where(l => l != "/Views/{1}/{0}.cshtml"));
        return result;
    }
}
