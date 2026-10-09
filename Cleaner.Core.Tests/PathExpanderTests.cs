using System;
using System.IO;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

public class PathExpanderTests
{
    [Fact]
    public void Expand_Temp_Returns_Valid_Path()
    {
        var expanded = PathExpander.Expand("%TEMP%");
        Assert.False(string.IsNullOrEmpty(expanded));

        var expected = Path.GetTempPath().TrimEnd('\\', '/');
        Assert.Equal(expected, expanded, ignoreCase: true);
    }

    [Fact]
    public void Expand_Windir_Returns_Windows_Directory()
    {
        var expanded = PathExpander.Expand("%WINDIR%");
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            expanded,
            ignoreCase: true);
    }

    [Fact]
    public void Expand_LocalAppData()
    {
        var expanded = PathExpander.Expand("%LOCALAPPDATA%");
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            expanded,
            ignoreCase: true);
    }

    [Fact]
    public void Expand_AppData()
    {
        var expanded = PathExpander.Expand("%APPDATA%");
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            expanded,
            ignoreCase: true);
    }

    [Fact]
    public void Expand_ProgramData()
    {
        var expanded = PathExpander.Expand("%PROGRAMDATA%");
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            expanded,
            ignoreCase: true);
    }

    [Fact]
    public void Expand_ProgramFiles()
    {
        var expanded = PathExpander.Expand("%PROGRAMFILES%");
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            expanded,
            ignoreCase: true);
    }

    [Fact]
    public void Expand_ProgramFilesX86()
    {
        var expanded = PathExpander.Expand("%PROGRAMFILES(X86)%");
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            expanded,
            ignoreCase: true);
    }

    [Fact]
    public void Expand_Composes_Path_With_Variable()
    {
        var expanded = PathExpander.Expand("%WINDIR%\\Temp");
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            expanded,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Temp", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expand_Multiple_Variables()
    {
        var expanded = PathExpander.Expand("%LOCALAPPDATA%\\Foo\\%USERPROFILE%");
        Assert.Contains(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            expanded,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            expanded,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expand_Unknown_Variable_Leaves_AsIs()
    {
        var expanded = PathExpander.Expand("%NOPE_NOT_A_REAL_VAR%\\sub");
        Assert.Contains("%NOPE_NOT_A_REAL_VAR%", expanded);
    }

    [Fact]
    public void Expand_Empty_Returns_Empty()
    {
        Assert.Equal("", PathExpander.Expand(null));
        Assert.Equal("", PathExpander.Expand(""));
        Assert.Equal("", PathExpander.Expand("   "));
    }

    [Fact]
    public void Expand_Without_Variables_Returns_AsIs()
    {
        Assert.Equal(@"C:\Some\Path", PathExpander.Expand(@"C:\Some\Path"));
    }

    [Fact]
    public void Expand_Trims_Trailing_Slash()
    {
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            PathExpander.Expand("%WINDIR%\\"),
            ignoreCase: true);
    }

    [Fact]
    public void Expand_Case_Insensitive()
    {
        var a = PathExpander.Expand("%windir%");
        var b = PathExpander.Expand("%WINDIR%");
        Assert.Equal(a, b, ignoreCase: true);
    }

    [Fact]
    public void ExpandAll_Maps_Array()
    {
        var list = PathExpander.ExpandAll(new[] { "%TEMP%", "%WINDIR%", "" });
        Assert.Equal(2, list.Count);
        Assert.Contains(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            list,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandAll_Null_Returns_Empty()
    {
        Assert.Empty(PathExpander.ExpandAll(null));
    }

    [Theory]
    [InlineData("*.log", true)]
    [InlineData("temp_??.tmp", true)]
    [InlineData(@"C:\clean\path", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainsWildcard_Detects(string? path, bool expected)
    {
        Assert.Equal(expected, PathExpander.ContainsWildcard(path));
    }
}