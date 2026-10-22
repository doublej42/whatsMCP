using AngleSharp.Html.Parser;

namespace WhatsBuildingMcp.Services;

/// <summary>Extracts readable text from the <c>/about</c> help page.</summary>
public static class AboutParser
{
    public static string ExtractText(string html, int maxLength = 8000)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = new HtmlParser().ParseDocument(html);
        var main = document.GetElementById("app-main-content")
            ?? document.QuerySelector("main")
            ?? document.GetElementById("content")
            ?? document.Body;
        var text = TextUtil.Normalize(main?.TextContent);
        return text.Length <= maxLength ? text : TextUtil.Truncate(text, maxLength);
    }
}
