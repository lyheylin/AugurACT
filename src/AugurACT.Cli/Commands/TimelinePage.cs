using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AugurACT.Core.Encounters;

namespace AugurACT.Cli.Commands;

// The plugin's own web page, with its styles, scripts and data inlined so it opens from disk. Each fight
// carries the same summary JSON the plugin sends, so the page reads both the same way.
internal static class TimelinePage
{
    private static readonly Regex Stylesheet = new("<link rel=\"stylesheet\" href=\"(?<href>[^\"]+)\">", RegexOptions.CultureInvariant);
    private static readonly Regex Script = new("<script src=\"(?<src>[^\"]+)\"></script>", RegexOptions.CultureInvariant);

    public static void Write(string path, string title, List<Fight> fights)
    {
        var web = Path.Combine(AppContext.BaseDirectory, "web");
        var page = File.ReadAllText(Path.Combine(web, "index.html"));
        page = Stylesheet.Replace(page, m => "<style>\n" + File.ReadAllText(Path.Combine(web, m.Groups["href"].Value)) + "</style>");

        // The data goes in ahead of the first script, which is before app.js reads it.
        var data = "<script type=\"application/json\" id=\"augur-data\">" + Data(title, fights).Replace("</", "<\\/") + "</script>\n";
        var first = true;
        page = Script.Replace(page, m =>
        {
            var inline = "<script>\n" + File.ReadAllText(Path.Combine(web, m.Groups["src"].Value)) + "</script>";
            if (first)
            {
                first = false;
                return data + inline;
            }

            return inline;
        });

        // The plugin's policy only allows bundled files; inline code needs its own.
        page = Regex.Replace(page, "<meta http-equiv=\"Content-Security-Policy\"[^>]*>",
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'\">");
        File.WriteAllText(path, page);
    }

    private static string Data(string title, List<Fight> fights)
    {
        var json = new StringBuilder();
        json.Append("{\"type\":\"timeline-report\",\"title\":").Append(JsonSerializer.Serialize(title)).Append(",\"fights\":[");
        for (var i = 0; i < fights.Count; i++)
        {
            var f = fights[i];
            if (i > 0)
            {
                json.Append(',');
            }

            json.Append("{\"title\":").Append(JsonSerializer.Serialize(f.Title))
                .Append(",\"zone\":").Append(JsonSerializer.Serialize(f.Zone))
                .Append(",\"source\":").Append(JsonSerializer.Serialize(f.Source))
                .Append(",\"start\":").Append(f.Start)
                .Append(",\"end\":").Append(f.End)
                .Append(",\"summary\":").Append(f.Summary.ToJson())
                .Append('}');
        }

        return json.Append("]}").ToString();
    }

    internal sealed record Fight(string Title, string? Zone, string Source, long Start, long End, EncounterSummary Summary);
}
