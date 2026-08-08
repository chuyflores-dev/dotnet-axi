using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

if (args.SequenceEqual(["--info"])
    && Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
{
    File.WriteAllText(
        Path.Combine(AppContext.BaseDirectory, "workspace-sdk.executed"),
        "executed");
    return 97;
}

if (IsOptionalDependencyShim())
{
    return OptionalDependencyShim(args);
}

if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "codex")
{
    return CodexDiscoveryProbe(args);
}

if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dnx")
{
    return DnxDiscoveryProbe(args, AppContext.BaseDirectory);
}

return args.FirstOrDefault() switch
{
    "echo" => Echo(args[1..]),
    "pressure" => await PressureAsync(args[1..]),
    "hang" => Hang(),
    "exit" => Exit(args[1..]),
    "signal" => Signal(args[1..]),
    "spawn-descendant" => SpawnDescendant(args[1..]),
    "descendant" => Hang(),
    "codex-fixture" => await CodexFixtureAsync(args[1..]),
    _ => 64,
};

static int OptionalDependencyShim(IReadOnlyList<string> values)
{
    var command = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
    if (command is null
        || (command != "git" && command != "rg"))
    {
        return 64;
    }

    var directory = AppContext.BaseDirectory;
    var markerPathFile = Path.Combine(directory, $"{command}.marker-path");
    if (File.Exists(markerPathFile))
    {
        var marker = File.ReadAllText(markerPathFile);
        File.AppendAllText(
            marker,
            JsonSerializer.Serialize(values) + Environment.NewLine);
    }

    var versionPath = Path.Combine(directory, $"{command}.version");
    var version = File.Exists(versionPath)
        ? File.ReadAllText(versionPath)
        : null;
    if (values.Count == 1
        && values[0] == "--version"
        && !string.IsNullOrWhiteSpace(version))
    {
        Console.WriteLine(version);
        return 0;
    }

    return int.TryParse(
        File.ReadAllText(Path.Combine(directory, $"{command}.exit-code")),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var exitCode)
            ? exitCode
            : 74;
}

static bool IsOptionalDependencyShim()
{
    var command = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
    return command is "git" or "rg"
        && File.Exists(Path.Combine(
            AppContext.BaseDirectory,
            $"{command}.exit-code"));
}

static int CodexDiscoveryProbe(IReadOnlyList<string> values)
{
    var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
    if (values.SequenceEqual(["--version"]))
    {
        Console.WriteLine(ReadProbeValue(
            codexHome,
            "probe-version.txt",
            "codex-cli 0.146.0"));
        return 0;
    }

    if (values.SequenceEqual(["login", "status"]))
    {
        Console.Error.WriteLine(ReadProbeValue(
            codexHome,
            "probe-authentication.txt",
            "Logged in using ChatGPT"));
        return 0;
    }

    if (values.Count == 5
        && values[0] == "-C"
        && values[2] == "debug"
        && values[3] == "prompt-input")
    {
        var skillPath = Path.Combine(
            values[1],
            ".agents",
            "skills",
            "dotnet-axi",
            "SKILL.md");
        var hidden = codexHome is not null
            && File.Exists(Path.Combine(
                codexHome,
                "probe-skill-hidden.txt"));
        var leaked = codexHome is not null
            && File.Exists(Path.Combine(
                codexHome,
                "probe-skill-leaked.txt"));
        var visibleSkillPath = File.Exists(skillPath)
            ? skillPath
            : Path.Combine(
                codexHome ?? values[1],
                "skills",
                "dotnet-axi",
                "SKILL.md");
        var description = File.Exists(visibleSkillPath)
            ? File.ReadLines(visibleSkillPath)
                .First(line => line.StartsWith(
                    "description: ",
                    StringComparison.Ordinal))["description: ".Length..]
            : "Use dotnet-axi for deterministic .NET repository evidence.";
        Console.WriteLine(JsonSerializer.Serialize(
            !hidden && (File.Exists(skillPath) || leaked)
                ? new[]
                {
                    $"- dotnet-axi: {description} (file: {visibleSkillPath})",
                }
                : []));
        return 0;
    }

    if (values.Count >= 10
        && string.Equals(values[0], "sandbox", StringComparison.Ordinal))
    {
        return CodexSandboxProbe(values);
    }

    return 64;
}

static int CodexSandboxProbe(IReadOnlyList<string> values)
{
    var delimiter = values.ToList().IndexOf("--");
    if (delimiter < 7
        || delimiter + 2 >= values.Count
        || !values.Take(delimiter).Contains(
            "dnaxi-benchmark",
            StringComparer.Ordinal)
        || !values.Take(delimiter).Any(value => value.Contains(
            "permissions={dnaxi-benchmark=",
            StringComparison.Ordinal)))
    {
        return 64;
    }

    var dnxExecutable = values[delimiter + 1];
    if (!string.Equals(
            Path.GetFileNameWithoutExtension(dnxExecutable),
            "dnx",
            StringComparison.Ordinal))
    {
        return 64;
    }

    return DnxDiscoveryProbe(
        values.Skip(delimiter + 2).ToArray(),
        Path.GetDirectoryName(dnxExecutable)
        ?? AppContext.BaseDirectory);
}

static int DnxDiscoveryProbe(
    IReadOnlyList<string> values,
    string stateDirectory)
{
    var exitCodePath = Path.Combine(
        stateDirectory,
        "dnx.exit-code");
    if (File.Exists(exitCodePath)
        && int.TryParse(
            File.ReadAllText(exitCodePath),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredExitCode)
        && configuredExitCode != 0)
    {
        return configuredExitCode;
    }

    if (values.Count != 7
        || !string.Equals(values[0], "dnaxi@0.4.0", StringComparison.Ordinal)
        || !string.Equals(values[1], "--source", StringComparison.Ordinal)
        || string.IsNullOrWhiteSpace(values[2])
        || !string.Equals(values[3], "--verbosity", StringComparison.Ordinal)
        || !string.Equals(values[4], "quiet", StringComparison.Ordinal)
        || !string.Equals(values[5], "--", StringComparison.Ordinal)
        || !string.Equals(values[6], "--version", StringComparison.Ordinal))
    {
        return 64;
    }

    Console.WriteLine("schema: dotnet-axi/v1");
    Console.WriteLine("command: version");
    Console.WriteLine("status: success");
    Console.WriteLine("tool_version: 0.4.0");
    return 0;
}

static string ReadProbeValue(
    string? directory,
    string fileName,
    string fallback)
{
    var path = directory is null ? null : Path.Combine(directory, fileName);
    return path is not null && File.Exists(path)
        ? File.ReadAllText(path).Trim()
        : fallback;
}

static async Task<int> CodexFixtureAsync(IReadOnlyList<string> values)
{
    var fixturePath = Environment.GetEnvironmentVariable("CODEX_FIXTURE_PATH");
    var behavior = Environment.GetEnvironmentVariable("CODEX_FIXTURE_BEHAVIOR")
        ?? "emit";
    var configuredExitCode =
        Environment.GetEnvironmentVariable("CODEX_FIXTURE_EXIT_CODE");
    for (var index = 0; index + 1 < values.Count; index += 2)
    {
        if (values[index] == "exec")
        {
            break;
        }

        switch (values[index])
        {
            case "--fixture":
                fixturePath = values[index + 1];
                break;
            case "--behavior":
                behavior = values[index + 1];
                break;
            case "--exit-code":
                configuredExitCode = values[index + 1];
                break;
        }
    }

    if (fixturePath is not null)
    {
        var encodedWorkspace = JsonSerializer.Serialize(
            Environment.CurrentDirectory);
        var content = (await File.ReadAllTextAsync(fixturePath)).Replace(
            "{{WORKSPACE}}",
            encodedWorkspace[1..^1],
            StringComparison.Ordinal);
        await Console.Out.WriteAsync(
            behavior == "truncate"
                ? content.TrimEnd('\r', '\n')
                : content);
        await Console.Out.FlushAsync();
    }

    if (behavior == "hang")
    {
        return Hang();
    }

    if (behavior == "stderr-denied")
    {
        await Console.Error.WriteLineAsync(
            "permission denied by the configured approval policy");
    }

    return int.TryParse(
        configuredExitCode,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var exitCode)
            ? exitCode
            : 0;
}

static int Echo(IReadOnlyList<string> values)
{
    Console.WriteLine($"cwd:{Encode(Environment.CurrentDirectory)}");
    Console.WriteLine(
        $"env:{Encode(Environment.GetEnvironmentVariable("PROCESS_RUNNER_VALUE") ?? "<null>")}");
    Console.WriteLine(
        $"path:{Encode(Environment.GetEnvironmentVariable("PATH") ?? "<null>")}");
    foreach (var value in values)
    {
        Console.WriteLine($"arg:{Encode(value)}");
    }

    return 0;
}

static async Task<int> PressureAsync(IReadOnlyList<string> values)
{
    if (values.Count != 1
        || !int.TryParse(
            values[0],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var count)
        || count < 0)
    {
        return 64;
    }

    var standardOutput = WriteAsync(Console.OpenStandardOutput(), (byte)'o', count);
    var standardError = WriteAsync(Console.OpenStandardError(), (byte)'e', count);
    await Task.WhenAll(standardOutput, standardError);
    return 0;
}

static async Task WriteAsync(Stream stream, byte value, int count)
{
    var buffer = Enumerable.Repeat(value, Math.Min(8192, count)).ToArray();
    var remaining = count;
    while (remaining > 0)
    {
        var length = Math.Min(buffer.Length, remaining);
        await stream.WriteAsync(buffer.AsMemory(0, length));
        remaining -= length;
    }

    await stream.FlushAsync();
}

static int Exit(IReadOnlyList<string> values) =>
    values.Count == 1
    && int.TryParse(
        values[0],
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var exitCode)
        ? exitCode
        : 64;

static int Signal(IReadOnlyList<string> values)
{
    if (OperatingSystem.IsWindows()
        || values.Count != 1
        || !int.TryParse(
            values[0],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var signal)
        || signal <= 0)
    {
        return 64;
    }

    return NativeMethods.Kill(Environment.ProcessId, signal) == 0
        ? Hang()
        : 71;
}

static int SpawnDescendant(IReadOnlyList<string> values)
{
    if (values.Count != 1 || values[0] is not ("exit-root" or "hang-root"))
    {
        return 64;
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "The process-test application path is unavailable."),
        WorkingDirectory = Environment.CurrentDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("descendant");
    using var descendant = Process.Start(startInfo)
        ?? throw new InvalidOperationException("The descendant did not start.");
    Console.WriteLine(
        $"descendant:{descendant.Id}:{descendant.StartTime.ToUniversalTime().Ticks}");
    Console.Out.Flush();
    return values[0] == "exit-root" ? 0 : Hang();
}

static int Hang()
{
    Thread.Sleep(Timeout.Infinite);
    return 70;
}

static string Encode(string value) =>
    Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

internal static class NativeMethods
{
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    internal static extern int Kill(int processId, int signal);
}
