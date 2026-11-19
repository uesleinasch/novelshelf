using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace NovelShelf.Core.Html;

public static partial class ChapterHtmlSanitizer
{
    private static readonly HashSet<string> Kept = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "hr", "em", "i", "strong", "b", "u", "s", "sub", "sup", "small",
        "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "ul", "ol", "li", "pre", "code",
    };

    private static readonly HashSet<string> Dropped = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "iframe", "object", "embed", "form", "button", "input",
        "select", "textarea", "svg", "img", "picture", "video", "audio", "template", "nav", "aside",
    };

    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase) { "br", "hr" };

    public static string ToXhtml(INode container)
    {
        var sb = new StringBuilder();
        foreach (var child in container.ChildNodes)
            Write(child, sb);
        var html = BlockGap().Replace(sb.ToString(), "$1\n");
        return EmptyParagraph().Replace(html, "").Trim();
    }

    private static void AppendEscaped(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '\u00A0': sb.Append("&#160;"); break;
                default: sb.Append(c); break;
            }
        }
    }

    private static void Write(INode node, StringBuilder sb)
    {
        switch (node)
        {
            case IText text:
                AppendEscaped(sb, text.Data);
                return;
            case IElement element when Dropped.Contains(element.LocalName):
                return;
            case IElement element when Kept.Contains(element.LocalName):
                var tag = element.LocalName.ToLowerInvariant();
                if (Void.Contains(tag))
                {
                    sb.Append('<').Append(tag).Append(" />");
                    return;
                }
                sb.Append('<').Append(tag).Append('>');
                foreach (var child in element.ChildNodes)
                    Write(child, sb);
                sb.Append("</").Append(tag).Append('>');
                if (IsBlock(tag))
                    sb.Append('\n');
                return;
            case IElement element:
                foreach (var child in element.ChildNodes)
                    Write(child, sb);
                return;
        }
    }

    private static bool IsBlock(string tag) =>
        tag is "p" or "blockquote" or "ul" or "ol" or "li" or "pre" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";

    [GeneratedRegex(@"(</(?:p|li|ul|ol|pre|blockquote|h[1-6])>)\s+")]
    private static partial Regex BlockGap();

    [GeneratedRegex(@"<p>(?:\s|&#160;)*</p>\n?")]
    private static partial Regex EmptyParagraph();
}
