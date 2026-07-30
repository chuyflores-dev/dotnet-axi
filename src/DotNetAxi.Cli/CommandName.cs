using System.CommandLine;
using System.CommandLine.Parsing;

namespace DotNetAxi.Cli;

internal static class CommandName
{
    public static string From(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        var segments = new List<string>();
        for (SymbolResult? result = parseResult.CommandResult;
             result is not null;
             result = result.Parent)
        {
            if (result is CommandResult commandResult &&
                commandResult.Command is not RootCommand)
            {
                segments.Add(commandResult.Command.Name);
            }
        }

        if (segments.Count == 0)
        {
            return "home";
        }

        segments.Reverse();
        return string.Join(' ', segments);
    }
}
