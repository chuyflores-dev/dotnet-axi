using DotNetAxi.Axi;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Tests;

public sealed class CanonicalInvocationTests
{
    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.4.0-alpha.1")]
    public void Guidance_pins_exact_stable_and_prerelease_versions(
        string version)
    {
        var guidance = AgentGuidanceCatalog.ForVersion(version);

        Assert.Equal(
            $"dnx dnaxi@{version} --verbosity quiet -- <command>",
            guidance.Invocation);
        Assert.Equal(
            $"dnx dnaxi@{version} --verbosity quiet -- --version",
            guidance.VersionInvocation);
        Assert.DoesNotContain("dnx dotnet-axi", guidance.Invocation);
    }

    [Fact]
    public void Installed_invocation_becomes_exact_dnx_invocation()
    {
        var result = CanonicalInvocation.OneShot(
            "dnaxi search file Program.cs");

        Assert.Equal(
            $"dnx dnaxi@{ToolVersion.Current} --verbosity quiet -- search file Program.cs",
            result);
    }

    [Fact]
    public void Structured_suggestion_uses_dnx_as_the_command()
    {
        var result = CanonicalInvocation.OneShot(
            new ResultSuggestion("dnaxi", ["--help"]));

        Assert.Equal("dnx", result.Command);
        Assert.Equal(
            [
                $"dnaxi@{ToolVersion.Current}",
                "--verbosity",
                "quiet",
                "--",
                "--help",
            ],
            result.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0.4.0 latest")]
    [InlineData("dnaxi@0.4.0")]
    public void Guidance_rejects_non_version_package_input(string version)
    {
        Assert.Throws<ArgumentException>(
            () => AgentGuidanceCatalog.ForVersion(version));
    }
}
