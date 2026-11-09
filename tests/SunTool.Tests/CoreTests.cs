using SunTool.Core.FileSystem;
using SunTool.Core.Settings;
using SunTool.Core.Text;

namespace SunTool.Tests;

public class CoreTests
{
    [Fact]
    public void NaturalComparer_SortsNumbersByValue()
    {
        var names = new List<string> { "file10.txt", "file2.txt", "file1.txt", "File3.txt" };
        names.Sort(NaturalStringComparer.Instance);
        Assert.Equal(new[] { "file1.txt", "file2.txt", "File3.txt", "file10.txt" }, names);
    }

    [Theory]
    [InlineData("jpg;png", "a.JPG", true)]
    [InlineData("*.jpg, *.png", "a.png", true)]
    [InlineData(".txt", "a.jpg", false)]
    [InlineData("", "anything.bin", true)]
    public void ExtensionFilter_Parses(string filter, string fileName, bool expected) =>
        Assert.Equal(expected, FileExtensionFilter.Parse(filter).Matches(fileName));

    [Theory]
    [InlineData("good name.txt", true)]
    [InlineData("bad:name.txt", false)]
    [InlineData("CON.txt", false)]
    [InlineData("trailing.", false)]
    [InlineData("", false)]
    public void FileNameValidator_Works(string name, bool valid) =>
        Assert.Equal(valid, FileNameValidator.Validate(name) is null);

    [Fact]
    public void IsSameOrSubPath_DetectsNesting()
    {
        Assert.True(PathUtil.IsSameOrSubPath(@"C:\a\b", @"C:\a"));
        Assert.True(PathUtil.IsSameOrSubPath(@"C:\a\", @"C:\A"));
        Assert.False(PathUtil.IsSameOrSubPath(@"C:\ab", @"C:\a"));
    }

    [Fact]
    public void SettingsService_RoundTrips()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "settings.json");

        var settings = new JsonSettingsService(path);
        settings.Set("Test.Value", new Dictionary<string, int> { ["한글"] = 3 });
        settings.Save();

        var reloaded = new JsonSettingsService(path);
        var value = reloaded.Get("Test.Value", new Dictionary<string, int>());
        Assert.Equal(3, value["한글"]);
        Assert.Equal(42, reloaded.Get("Missing", 42));
    }
}
