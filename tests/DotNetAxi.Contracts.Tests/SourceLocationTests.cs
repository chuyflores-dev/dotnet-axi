using DotNetAxi.Contracts;

namespace DotNetAxi.Contracts.Tests;

public sealed class SourceLocationTests
{
    [Fact]
    public void One_based_coordinates_and_external_scope_are_explicit()
    {
        var location = new SourceLocation(
            "../external/Café😀.cs",
            line: 7,
            column: 4,
            isExternal: true);

        Assert.Equal("../external/Café😀.cs", location.Path);
        Assert.Equal(7, location.Line);
        Assert.Equal(4, location.Column);
        Assert.True(location.IsExternal);
    }

    [Fact]
    public void Zero_based_utf16_coordinates_convert_without_scalar_recounting()
    {
        const string line = "A😀B";
        var zeroBasedUtf16Column = line.IndexOf('B');

        var location = SourceLocation.FromZeroBasedUtf16(
            "src\\Unicode\\Café😀.cs",
            zeroBasedLine: 1,
            zeroBasedColumn: zeroBasedUtf16Column);

        Assert.Equal("src/Unicode/Café😀.cs", location.Path);
        Assert.Equal(2, location.Line);
        Assert.Equal(4, location.Column);
        Assert.False(location.IsExternal);
    }

    [Theory]
    [InlineData(0, 1, "line")]
    [InlineData(1, 0, "column")]
    [InlineData(-1, 1, "line")]
    [InlineData(1, -1, "column")]
    public void One_based_coordinates_reject_zero_and_negative_values(
        int line,
        int column,
        string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SourceLocation("src/File.cs", line, column));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(-1, 0, "zeroBasedLine")]
    [InlineData(0, -1, "zeroBasedColumn")]
    [InlineData(int.MaxValue, 0, "zeroBasedLine")]
    [InlineData(0, int.MaxValue, "zeroBasedColumn")]
    public void Zero_based_coordinates_must_fit_one_based_output(
        int line,
        int column,
        string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => SourceLocation.FromZeroBasedUtf16(
                "src/File.cs",
                line,
                column));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData("/absolute/File.cs")]
    [InlineData("C:\\absolute\\File.cs")]
    [InlineData("\\\\server\\share\\File.cs")]
    public void Absolute_source_paths_are_rejected(string path)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new SourceLocation(path, 1, 1));

        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void External_source_paths_require_the_external_label()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new SourceLocation("../external/File.cs", 1, 1));

        Assert.Equal("isExternal", exception.ParamName);
    }

    [Fact]
    public void Source_paths_are_lexically_normalized_before_scope_validation()
    {
        var location = new SourceLocation(
            "./src/generated/../Unicode//Café😀.cs",
            1,
            1);

        Assert.Equal("src/Unicode/Café😀.cs", location.Path);
    }

    [Fact]
    public void Nested_parent_segments_cannot_hide_an_external_path()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new SourceLocation(
                "src/../../external/File.cs",
                1,
                1));
        var external = new SourceLocation(
            "src/../../external/File.cs",
            1,
            1,
            isExternal: true);

        Assert.Equal("isExternal", exception.ParamName);
        Assert.Equal("../external/File.cs", external.Path);
        Assert.True(external.IsExternal);
    }

    [Theory]
    [InlineData("C:relative\\File.cs")]
    [InlineData("z:relative/File.cs")]
    public void Drive_qualified_source_paths_are_rejected(string path)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new SourceLocation(path, 1, 1));

        Assert.Equal("path", exception.ParamName);
    }
}
