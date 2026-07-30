using System.Reflection;

namespace DotNetAxi.Cli;

internal static class ToolVersion
{
    private const string MetadataKey = "DotNetAxi.ToolVersion";

    public static string Current { get; } = FromAssembly(
        typeof(Program).Assembly);

    internal static string FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var version = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute =>
                string.Equals(
                    attribute.Key,
                    MetadataKey,
                    StringComparison.Ordinal))
            ?.Value;

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException(
                $"Assembly metadata '{MetadataKey}' is missing.");
        }

        return version;
    }
}
