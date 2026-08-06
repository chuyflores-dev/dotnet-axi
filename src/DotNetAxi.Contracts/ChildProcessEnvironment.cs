using System.Collections.ObjectModel;

namespace DotNetAxi.Contracts;

public static class ChildProcessEnvironment
{
    public static IReadOnlyDictionary<string, string> DotNetDefaults { get; } =
        ReadOnly(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "0",
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_CLI_UI_LANGUAGE"] = "en-US",
                ["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true",
                ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
                ["DOTNET_NOLOGO"] = "1",
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["NO_COLOR"] = "1",
            });

    public static IReadOnlyDictionary<string, string> RipgrepDefaults { get; } =
        ReadOnly(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LC_ALL"] = "C",
                ["NO_COLOR"] = "1",
            });

    private static IReadOnlyDictionary<string, string> ReadOnly(
        Dictionary<string, string> environment) =>
        new ReadOnlyDictionary<string, string>(environment);
}
