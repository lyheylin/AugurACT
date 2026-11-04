using System.Text.RegularExpressions;

namespace AugurACT.Cli.Commands;

// Finding saved logs, and what their names say.
internal static class LogFiles
{
    private static readonly Regex LogName = new(@"^eq2log_(?<name>[^.]+)", RegexOptions.CultureInvariant);

    // Files and folders (every eq2log_*.txt inside), optionally only those written since a date.
    public static List<string> Expand(IEnumerable<string> paths, DateTime? since)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "eq2log_*.txt"));
            }
            else if (File.Exists(path))
            {
                files.Add(path);
            }
            else
            {
                Console.Error.WriteLine($"not found: {path}");
            }
        }

        return files
            .Where(f => since == null || File.GetLastWriteTime(f) >= since)
            .Distinct()
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    // The logging character, from the eq2log_<Character> file name.
    public static string? LoggerName(string path)
    {
        var name = LogName.Match(Path.GetFileName(path));
        return name.Success ? name.Groups["name"].Value : null;
    }
}
