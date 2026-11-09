using System.Text.Json;
using SunTool.Core.Settings;
using SunTool.Modules.BatchRename.Logic;
using SunTool.Modules.BatchRename.Logic.Rules;

namespace SunTool.Tests;

public class BatchRenameTests
{
    private static RenameItem Item(string path) => new(path, 0, new DateTime(2024, 5, 1), new DateTime(2024, 1, 1));

    private static RenameContext Ctx(int index = 0, string path = @"C:\t\photo.jpg") => new(Item(path), index, index, 10);

    [Theory]
    [InlineData(TextPosition.Prefix, 0, false, "", "NEW_photo")]
    [InlineData(TextPosition.Suffix, 0, false, "", "photoNEW_")]
    [InlineData(TextPosition.AtIndex, 2, false, "", "phNEW_oto")]
    [InlineData(TextPosition.AtIndex, 1, true, "", "photNEW_o")]
    [InlineData(TextPosition.BeforeText, 0, false, "to", "phoNEW_to")]
    [InlineData(TextPosition.AfterText, 0, false, "PHO", "phoNEW_to")]
    [InlineData(TextPosition.AfterText, 0, false, "zzz", "photo")]
    public void InsertText_Positions(TextPosition position, int index, bool fromEnd, string anchor, string expected)
    {
        var rule = new InsertTextRule { Text = "NEW_", Position = position, Index = index, FromEnd = fromEnd, AnchorText = anchor };
        Assert.Equal(expected, rule.Apply("photo", Ctx()));
    }

    [Fact]
    public void Numbering_UsesFormatPaddingAndOrder()
    {
        var rule = new NumberingRule { Start = 1, Step = 2, Padding = 3, Format = "_{n}", Position = TextPosition.Suffix };
        Assert.Equal("photo_001", rule.Apply("photo", Ctx(0)));
        Assert.Equal("photo_005", rule.Apply("photo", Ctx(2)));

        rule.ReplaceName = true;
        rule.Format = "IMG-{n}";
        Assert.Equal("IMG-003", rule.Apply("photo", Ctx(1)));
    }

    [Fact]
    public void Replace_PlainAndRegex()
    {
        var plain = new ReplaceRule { Find = "o", ReplaceWith = "0" };
        Assert.Equal("ph0t0", plain.Apply("photo", Ctx()));

        var regex = new ReplaceRule { Find = @"(\d{4})(\d{2})", ReplaceWith = "$1-$2", UseRegex = true };
        Assert.Equal("img_2024-05", regex.Apply("img_202405", Ctx()));

        var invalid = new ReplaceRule { Find = "(", UseRegex = true };
        Assert.Throws<RenameRuleException>(() => invalid.Apply("x", Ctx()));
    }

    [Fact]
    public void Remove_TextAndRange()
    {
        Assert.Equal("photo", new RemoveRule { Mode = RemoveMode.Text, Text = "_copy" }.Apply("photo_copy", Ctx()));
        Assert.Equal("oto", new RemoveRule { Mode = RemoveMode.Range, StartIndex = 0, Length = 2 }.Apply("photo", Ctx()));
        Assert.Equal("ph", new RemoveRule { Mode = RemoveMode.Range, StartIndex = 0, Length = 3, FromEnd = true }.Apply("photo", Ctx()));
    }

    [Fact]
    public void Template_ResolvesTokens()
    {
        var rule = new TemplateRule { Template = "{parent}_{date:yyMMdd}_{n:00}_{name}", Start = 1 };
        Assert.Equal("t_240501_03_photo", rule.Apply("photo", Ctx(2)));
    }

    [Fact]
    public void Engine_KeepsExtension_AndDetectsDuplicates()
    {
        var items = new[] { Item(@"C:\t\a.jpg"), Item(@"C:\t\b.jpg") };
        var rules = new IRenameRule[] { new ReplaceRule { Find = "a", ReplaceWith = "b" } };

        var previews = RenameEngine.Preview(items, rules, includeExtension: false, _ => new[] { "a.jpg", "b.jpg" });

        // a → b 는 b.jpg 와 겹치지만, b.jpg 는 바뀌지 않으므로 충돌
        Assert.Equal("b.jpg", previews[0].NewName);
        Assert.Equal(RenameStatus.Conflict, previews[0].Status);
        Assert.Equal(RenameStatus.Unchanged, previews[1].Status);
    }

    [Fact]
    public void Engine_AllowsSwapAndCaseOnlyChange()
    {
        var items = new[] { Item(@"C:\t\a.jpg"), Item(@"C:\t\b.jpg"), Item(@"C:\t\c.jpg") };
        var rules = new IRenameRule[] { new SwapRule() };

        var previews = RenameEngine.Preview(items, rules, includeExtension: false, _ => new[] { "a.jpg", "b.jpg", "c.jpg" });

        Assert.All(previews, p => Assert.Equal(RenameStatus.Ok, p.Status));
        Assert.Equal("C.jpg", previews[2].NewName);
    }

    [Fact]
    public void Engine_FlagsInvalidNames()
    {
        var items = new[] { Item(@"C:\t\a.jpg") };
        var previews = RenameEngine.Preview(items, new IRenameRule[] { new InsertTextRule { Text = "?" } }, false, _ => Array.Empty<string>());
        Assert.Equal(RenameStatus.Invalid, previews[0].Status);
    }

    [Fact]
    public void Executor_SwapsNamesSafely_AndUndoRestores()
    {
        using var temp = new TempFolder();
        var a = temp.File("a.txt", "A");
        var b = temp.File("b.txt", "B");

        var result = RenameExecutor.Execute(new[] { new RenameOperation(a, b), new RenameOperation(b, a) });
        Assert.Equal(2, result.Completed.Count);
        Assert.Equal("B", File.ReadAllText(a));
        Assert.Equal("A", File.ReadAllText(b));

        var journal = new RenameJournal(DateTime.Now, result.Completed.ToList());
        RenameExecutor.Execute(journal.CreateUndoOperations());
        Assert.Equal("A", File.ReadAllText(a));
        Assert.Equal("B", File.ReadAllText(b));
    }

    [Fact]
    public void Rules_SerializePolymorphically()
    {
        var rules = new List<RenameRuleBase>
        {
            new InsertTextRule { Text = "X", Position = TextPosition.AfterText, AnchorText = "_" },
            new NumberingRule { Padding = 4 },
        };

        var json = JsonSerializer.Serialize(rules, JsonSettingsService.SerializerOptions);
        var restored = JsonSerializer.Deserialize<List<RenameRuleBase>>(json, JsonSettingsService.SerializerOptions)!;

        var insert = Assert.IsType<InsertTextRule>(restored[0]);
        Assert.Equal(TextPosition.AfterText, insert.Position);
        Assert.Equal(4, Assert.IsType<NumberingRule>(restored[1]).Padding);
    }

    [Fact]
    public void Sort_NaturalOrder()
    {
        var items = new[] { Item(@"C:\t\img10.jpg"), Item(@"C:\t\img2.jpg"), Item(@"C:\t\img1.jpg") };
        var sorted = FileCollector.Sort(items, SortKey.NaturalName, descending: false);
        Assert.Equal(new[] { "img1.jpg", "img2.jpg", "img10.jpg" }, sorted.Select(i => i.FileName));
    }

    /// <summary>a↔b 교환, c→C (대소문자만) 테스트용 규칙.</summary>
    private sealed class SwapRule : IRenameRule
    {
        public bool IsEnabled => true;

        public string Apply(string name, RenameContext context) => name switch
        {
            "a" => "b",
            "b" => "a",
            "c" => "C",
            _ => name,
        };
    }
}
