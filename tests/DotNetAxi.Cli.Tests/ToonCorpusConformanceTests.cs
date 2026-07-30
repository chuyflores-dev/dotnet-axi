using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DotNetAxi.Cli.Output;

namespace DotNetAxi.Cli.Tests;

public sealed class ToonCorpusConformanceTests
{
    private static readonly IReadOnlyDictionary<string, string> CorpusHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["arrays-nested.json"] =
                "e30b3def09d2df11971f15b87c3e6cd8c5b8a375cc9036c7ae0fdf755d07f86b",
            ["arrays-objects.json"] =
                "a4829052e36a365624526816b7d8f9ddae10e7df0e93eaf6b089603e7f302b7a",
            ["arrays-primitive.json"] =
                "0ab7c14b9d5038ed52f4449680ccda54081867bfef0b383816aeebc069d8054f",
            ["arrays-tabular.json"] =
                "c6a4079a188533fd3983363c80816e69dbd82cfd3abf4497c7b5bd0c32d8e94d",
            ["delimiters.json"] =
                "db6b0b8a6695e2194122f3e035dd4d0481d7f90068090f34c96f02fa0c7f0166",
            ["objects-keyed.json"] =
                "b064d9fe983c737d1273c05db4b4f38889e04c341637b0d0d33d681c1f5877d0",
            ["objects.json"] =
                "94703db9232df9ea09b3543733fa1cb34379d3cdc8b6d9db56196d9afc2954c5",
            ["primitives.json"] =
                "19c54507f5a08d6d8f6c9907c5ed831097489963f8a33e8847ab94ef857e929e",
            ["whitespace.json"] =
                "581d0fdf6c2f433b7a4c48b123a471346245249d1bb15352885f7ce51865dcfe",
        };

    public static TheoryData<string, int, string> CanonicalProfileCases
    {
        get
        {
            var cases = new TheoryData<string, int, string>();
            foreach (var file in CorpusFiles())
            {
                var tests = ReadFixture(file)["tests"]!.AsArray();
                for (var index = 0; index < tests.Count; index++)
                {
                    var test = tests[index]!.AsObject();
                    if (UsesCanonicalProfile(test))
                    {
                        cases.Add(
                            Path.GetFileName(file),
                            index,
                            test["name"]!.GetValue<string>());
                    }
                }
            }

            return cases;
        }
    }

    [Fact]
    public void Vendored_corpus_matches_the_v4_1_pin_and_expected_inventory()
    {
        var files = CorpusFiles();
        Assert.Equal(
            CorpusHashes.Keys.Order(StringComparer.Ordinal),
            files.Select(Path.GetFileName));

        var total = 0;
        var canonical = 0;
        var optionSpecific = 0;
        var v41Cases = 0;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var hash = Convert.ToHexStringLower(
                SHA256.HashData(File.ReadAllBytes(file)));
            Assert.Equal(CorpusHashes[name], hash);

            var fixture = ReadFixture(file);
            Assert.Equal("4.0", fixture["version"]!.GetValue<string>());
            Assert.Equal("encode", fixture["category"]!.GetValue<string>());

            foreach (var node in fixture["tests"]!.AsArray())
            {
                var test = node!.AsObject();
                total++;
                if (UsesCanonicalProfile(test))
                {
                    canonical++;
                }
                else
                {
                    optionSpecific++;
                }

                if (test["minSpecVersion"]?.GetValue<string>() is "4.1")
                {
                    v41Cases++;
                }
            }
        }

        Assert.Equal(179, total);
        Assert.Equal(156, canonical);
        Assert.Equal(23, optionSpecific);
        Assert.Equal(3, v41Cases);
    }

    [Theory]
    [MemberData(nameof(CanonicalProfileCases))]
    public void Production_encoder_matches_canonical_profile_case(
        string fileName,
        int testIndex,
        string caseName)
    {
        _ = caseName;
        var fixture = ReadFixture(Path.Combine(CorpusRoot(), "encode", fileName));
        var test = fixture["tests"]![testIndex]!.AsObject();
        var input = test["input"]?.DeepClone();
        var expected = test["expected"]!.GetValue<string>();

        var actual = ToonV41Encoder.Encode(input);

        Assert.Equal(expected, actual);
    }

    private static bool UsesCanonicalProfile(JsonObject test)
    {
        var options = test["options"] as JsonObject;
        var delimiter =
            options?["delimiter"]?.GetValue<string>() ?? ",";
        var indentSize =
            options?["indentSize"]?.GetValue<int>() ?? 2;
        return delimiter is "," && indentSize == 2;
    }

    private static string[] CorpusFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(CorpusRoot(), "encode"),
                "*.json",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static JsonObject ReadFixture(string path) =>
        JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static string CorpusRoot() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Conformance",
            "toon-spec-v4.1.0");
}
