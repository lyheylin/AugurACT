using System.Globalization;
using AugurACT.Cli.Commands;

const string Usage = """
    usage:
      AugurACT.Cli summary <eq2log file>
      AugurACT.Cli barrage <log files or folders...> [--since yyyy-mm-dd] [--catalog file.json] [--html file.html]
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
                case "--html" when i + 1 < args.Length:
                    options.Html = args[++i];
                    break;
                default:
                    options.Paths.Add(args[i]);
                    break;
            }
        }

        return options.Paths.Count == 0 ? Fail() : BarrageCommand.Run(options);

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
