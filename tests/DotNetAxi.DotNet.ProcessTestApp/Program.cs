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

return args.FirstOrDefault() switch
{
    "echo" => Echo(args[1..]),
    "pressure" => await PressureAsync(args[1..]),
    "hang" => Hang(),
    "exit" => Exit(args[1..]),
    "signal" => Signal(args[1..]),
    "spawn-descendant" => SpawnDescendant(args[1..]),
    "descendant" => Hang(),
    _ => 64,
};

static int OptionalDependencyShim(IReadOnlyList<string> values)
{
    var command = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
    if (command is null || command is not ("git" or "rg"))
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
