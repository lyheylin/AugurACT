using System.Globalization;
using AugurACT.Cli.Commands;

const string Usage = """
    usage:
      AugurACT.Cli summary <eq2log file>
      AugurACT.Cli barrage <log files or folders...> [--since yyyy-mm-dd] [--catalog file.json]
      AugurACT.Cli deaths <log files or folders...> [--since yyyy-mm-dd]
      AugurACT.Cli timeline <log files or folders...> --html file.html [--since yyyy-mm-dd] [--data encounters.json]
      AugurACT.Cli texts <log files or folders...> [--since yyyy-mm-dd] [--data encounters.json]
    """;

if (args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

switch (args[0])
{
    case "summary" when args.Length == 2:
        return SummaryCommand.Run(args[1]);

    case "barrage":
        var options = new BarrageCommand.Options();
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--since" when i + 1 < args.Length:
                    options.Since = DateTime.ParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    break;
                case "--catalog" when i + 1 < args.Length:
                    options.Catalog = args[++i];
                    break;
                default:
                    options.Paths.Add(args[i]);
                    break;
            }
        }

        return options.Paths.Count == 0 ? Fail() : BarrageCommand.Run(options);

    case "deaths":
    {
        var (paths, since, _, _) = ReadLogArgs();
        return paths.Count == 0 ? Fail() : DeathsCommand.Run(paths, since);
    }

    case "timeline":
    {
        var (paths, since, html, data) = ReadLogArgs();
        return paths.Count == 0 || html == null ? Fail() : TimelineCommand.Run(paths, since, html, data);
    }

    case "texts":
    {
        var (paths, since, _, data) = ReadLogArgs();
        return paths.Count == 0 ? Fail() : TextsCommand.Run(paths, since, data);
    }

    // The original form, kept so existing notes still work.
    case var _ when args.Length == 1 && File.Exists(args[0]):
        return SummaryCommand.Run(args[0]);

    default:
        return Fail();
}

int Fail()
{
    Console.Error.WriteLine(Usage);
    return 2;
}

// Paths, plus --since, --html and --data (the encounters data file, for its blacklist).
(List<string> Paths, DateTime? Since, string? Html, string? Data) ReadLogArgs()
{
    string? data = null;
    var paths = new List<string>();
    DateTime? since = null;
    string? html = null;
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--since" && i + 1 < args.Length)
        {
            since = DateTime.ParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else if (args[i] == "--html" && i + 1 < args.Length)
        {
            html = args[++i];
        }
        else if (args[i] == "--data" && i + 1 < args.Length)
        {
            data = args[++i];
        }
        else
        {
            paths.Add(args[i]);
        }
    }

    return (paths, since, html, data);
}
