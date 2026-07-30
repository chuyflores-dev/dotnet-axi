using System.Text;
using DotNetAxi.Cli.Output;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Tests;

public sealed class ToonFuzzConformanceTests
{
    private const uint Seed = 0xD07A_4100;

    private readonly ToonResultSerializer _serializer = new();

    [Fact]
    public void Untrusted_string_corpus_preserves_wire_invariants()
    {
        var values = CreateValues();
        var rows = values
            .Select((value, index) => new FuzzRow(
                index,
                value,
                new NestedValue(
                    $"left:{value}",
                    $"right,{value}")))
            .ToArray();
        var map = values
            .Take(32)
            .Select((value, index) => new KeyValuePair<string, string>(
                $"key[{index}]:{value}",
                value))
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
        var result = CommandResult<FuzzPayload>.Success(
            "conformance fuzz",
            new FuzzPayload(
                Seed,
                values.Count,
                values,
                rows,
                map));

        var bytes = _serializer.SerializeToUtf8(result);
        var document = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(bytes);

        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.DoesNotContain('\r', document);
        Assert.False(document.EndsWith('\n'));
        Assert.DoesNotContain(
            document,
            static character =>
                character < ' ' &&
                character is not '\n');
        Assert.Contains($"values[{values.Count}]:", document);

        var rowHeader =
            $"rows[{rows.Length}]{{index,value,nested{{left,right}}}}:";
        var lines = document.Split('\n');
        var rowHeaderIndex = Array.IndexOf(lines, rowHeader);
        var mapIndex = Array.IndexOf(lines, "map:");
        Assert.True(rowHeaderIndex >= 0);
        Assert.True(mapIndex > rowHeaderIndex);

        var encodedRows = lines[(rowHeaderIndex + 1)..mapIndex];
        Assert.Equal(rows.Length, encodedRows.Length);
        Assert.All(
            encodedRows,
            static row =>
            {
                Assert.StartsWith("  ", row, StringComparison.Ordinal);
                Assert.Equal(4, CountCells(row.AsSpan(2)));
            });

        var outputDirectory = Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "toon-conformance");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(
            Path.Combine(outputDirectory, "fuzz-untrusted-strings.toon"),
            bytes);
    }

    private static IReadOnlyList<string> CreateValues()
    {
        var values = new List<string>
        {
            string.Empty,
            "plain",
            "true",
            "false",
            "null",
            "05",
            "- item",
            "# comment-like",
            " leading",
            "trailing ",
            "comma,value",
            "colon:value",
            "[array]{shape}",
            "\"quoted\"",
            "slash\\path",
            "line1\nline2",
            "tab\there",
            "return\rhere",
            string.Concat(
                Enumerable
                    .Range(0, 32)
                    .Select(static value => (char)value)),
            "\u007f\u0085\u009f",
            "café",
            "你好",
            "مرحبا",
            "e\u0301",
            "\u2028\u2029",
            "🚀👩🏽‍💻",
        };
        var atoms = values
            .Where(static value => value.Length > 0)
            .Concat(
            [
                ",",
                ":",
                "\"",
                "\\",
                "[",
                "]",
                "{",
                "}",
                "\n",
                "\r",
                "\t",
                "\0",
            ])
            .ToArray();
        var state = Seed;

        for (var index = 0; index < 96; index++)
        {
            var count = (int)(Next(ref state) % 12);
            var value = new StringBuilder();
            for (var atomIndex = 0; atomIndex < count; atomIndex++)
            {
                value.Append(atoms[Next(ref state) % atoms.Length]);
            }

            values.Add(value.ToString());
        }

        return values.AsReadOnly();
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static int CountCells(ReadOnlySpan<char> row)
    {
        var cells = 1;
        var quoted = false;
        var escaped = false;

        foreach (var character in row)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (quoted && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && character == ',')
            {
                cells++;
            }
        }

        Assert.False(quoted);
        Assert.False(escaped);
        return cells;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-axi.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find dotnet-axi.slnx above {AppContext.BaseDirectory}.");
    }

    private sealed record FuzzPayload(
        uint Seed,
        int CaseCount,
        IReadOnlyList<string> Values,
        IReadOnlyList<FuzzRow> Rows,
        IReadOnlyDictionary<string, string> Map);

    private sealed record FuzzRow(
        int Index,
        string Value,
        NestedValue Nested);

    private sealed record NestedValue(string Left, string Right);
}
