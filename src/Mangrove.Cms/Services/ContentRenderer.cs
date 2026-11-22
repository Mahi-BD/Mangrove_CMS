using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Html;

namespace Mangrove.Cms.Services;

/// <summary>Turns stored Markdown into safe HTML. Everything user-authored goes through here.</summary>
public partial class ContentRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private readonly HtmlSanitizer _sanitizer;

    public ContentRenderer()
    {
        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedTags.UnionWith(new[] { "figure", "figcaption", "iframe", "video", "audio", "source", "details", "summary", "mark" });
        _sanitizer.AllowedAttributes.UnionWith(new[] { "class", "id", "loading", "controls", "allowfullscreen", "frameborder", "allow" });
        _sanitizer.AllowedSchemes.Add("mailto");
        // Only allow embeds from well-known video hosts.
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlInlineFrameElement frame)
            {
                var src = frame.Source ?? "";
                if (!AllowedEmbed().IsMatch(src)) frame.Remove();
            }
        };
    }

    public HtmlString Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return HtmlString.Empty;
        var html = Markdown.ToHtml(markdown, _pipeline);
        return new HtmlString(_sanitizer.Sanitize(html));
    }

    /// <summary>Comments: Markdown without raw HTML, then sanitized.</summary>
    public HtmlString RenderComment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return HtmlString.Empty;
        var pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
        var html = Markdown.ToHtml(text, pipeline);
        return new HtmlString(_sanitizer.Sanitize(html));
    }

    /// <summary>Plain-text summary for listings, feeds and meta descriptions.</summary>
    public string PlainText(string? markdown, int maxLength = 240)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var text = Markdown.ToPlainText(markdown, _pipeline);
        text = WebUtility.HtmlDecode(Tags().Replace(text, " "));
        text = Spaces().Replace(text, " ").Trim();
        if (text.Length <= maxLength) return text;
        var cut = text.LastIndexOf(' ', maxLength);
        return text[..(cut > maxLength / 2 ? cut : maxLength)].TrimEnd(',', '.', ';', ':') + "…";
    }

    [GeneratedRegex(@"^https://(www\.)?(youtube\.com|youtube-nocookie\.com|player\.vimeo\.com)/", RegexOptions.IgnoreCase)]
    private static partial Regex AllowedEmbed();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
