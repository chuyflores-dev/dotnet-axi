namespace DotNetAxi.Testing.Tests;

public sealed class FixtureContentHashTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
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
            ["catalog-vstest"] =
                "ea43eda264105676c44eabb30944cc040eee7c55556473f5bf2b4316ff70d910",
        };

    [Fact]
    public async Task Catalog_content_hashes_are_checkout_independent()
    {
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
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
    }

    private static string CatalogRoot() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Catalog");
}
