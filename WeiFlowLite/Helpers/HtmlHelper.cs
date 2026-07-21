using System.Text.RegularExpressions;

namespace WeiFlowLite.Helpers
{
    public static class HtmlHelper
    {
        public static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;

            html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<[^>]+>", "");
            html = System.Net.WebUtility.HtmlDecode(html);
            html = Regex.Replace(html, @"\n\s+", "\n");
            html = Regex.Replace(html, @"\n{3,}", "\n\n");
            html = html.Trim();

            return html;
        }
    }
}