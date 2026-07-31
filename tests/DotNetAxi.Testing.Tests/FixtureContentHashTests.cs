namespace DotNetAxi.Testing.Tests;

public sealed class FixtureContentHashTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["catalog-ambiguous-solution"] =
                "75002de1affb93dceecbca1454e5e740a228d260bfba0a31eee6d96e94921ec7",
            ["catalog-broken-project"] =
                "07e685b66e1ebc989e7567492714f5db441632795ea0d35f45668d1683f691ba",
            ["catalog-external-import"] =
                "2bf2b56395ce080cea80a02b45656c265c42738477dceac4ac79f1aec57c5eaf",
            ["catalog-git-conflict"] =
                "5a2ec870a3a651414a100f81356bc77235069ff30ef6b36e972a4301b178e14e",
            ["catalog-git-worktree"] =
                "c09c5da0a9b9a8937dff88e82785d6f192a33bb975045ae12046afd44e46600e",
            ["catalog-missing-assets"] =
                "35438c7eecaf87121a7ea4c1f17cfbee9e044f097d0312198ca3e2d1bbaaf60d",
            ["catalog-mtp"] =
                "700d3fdbc7c875ef768aef51b4c2279e309999d1c87dea22e6d1fc4d28fad76a",
            ["catalog-multi-project-sln"] =
                "7cfddc54a66a4355f251191bc6b021d77b85845fa8d3f0e6025d2d6cef69961e",
            ["catalog-project-cycle"] =
                "f3548431fcdf5973effe46d838a47e51890fe2508bd256f4b9d9299eee7f0b31",
            ["catalog-rich-slnx"] =
                "e42e9c93a58fd239457a75632a36b60ae040473fba5226c04f0d8514b6694e85",
            ["catalog-single-project"] =
                "fe1821967b0fe56a092db57787321b75b075f6307e110101238749e0d231296f",
            ["catalog-unsupported-input"] =
                "840b8b7cc2fe056a53ee0acdacb0a2dbb2451c909a55a57b1da9e126219a21ba",
            ["catalog-vstest"] =
                "ea43eda264105676c44eabb30944cc040eee7c55556473f5bf2b4316ff70d910",
        };

    private static readonly IReadOnlyDictionary<string, string>
        ExpectedExternalHashes =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["catalog-external-import"] =
                    "9e872eaca5a27609c1fc62b22fddd7b8b1a4b8e08d46dd9b1725573b290f3208",
            };

    [Fact]
    public async Task Catalog_content_hashes_are_checkout_independent()
    {
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        var actualExternal = new Dictionary<string, string>(
            StringComparer.Ordinal);
        var factory = new RepositoryFixtureFactory();

        foreach (var manifestPath in Directory
                     .EnumerateFiles(
                         CatalogRoot(),
                         "fixture.json",
                         SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            await using var fixture = await factory.CreateAsync(manifestPath);
            actual.Add(fixture.Identity.Name, fixture.ContentHash);
            if (fixture.ExternalContentHash is not null)
            {
                actualExternal.Add(
                    fixture.Identity.Name,
                    fixture.ExternalContentHash);
            }
        }

        Assert.True(
            ExpectedHashes.OrderBy(static pair => pair.Key)
                .SequenceEqual(actual.OrderBy(static pair => pair.Key)),
            string.Join(
                Environment.NewLine,
                actual
                    .OrderBy(static pair => pair.Key)
                    .Select(static pair =>
                        $"[\"{pair.Key}\"] = \"{pair.Value}\",")));
        Assert.True(
            ExpectedExternalHashes.OrderBy(static pair => pair.Key)
                .SequenceEqual(
                    actualExternal.OrderBy(static pair => pair.Key)),
            string.Join(
                Environment.NewLine,
                actualExternal
                    .OrderBy(static pair => pair.Key)
                    .Select(static pair =>
                        $"[\"{pair.Key}\"] = \"{pair.Value}\",")));
    }

    private static string CatalogRoot() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Catalog");
}
