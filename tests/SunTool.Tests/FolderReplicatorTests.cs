using SunTool.Modules.FolderReplicator.Logic;

namespace SunTool.Tests;

public class FolderReplicatorTests
{
    [Fact]
    public void Replicate_CreatesFullTree_AndCopiesOnlyDirectFilesOfSelectedFolders()
    {
        using var temp = new TempFolder();
        // Source/
        //   root.txt
        //   A/a1.txt
        //   A/Sub/deep.txt
        //   B/b1.txt
        temp.File(@"Source\root.txt");
        temp.File(@"Source\A\a1.txt");
        temp.File(@"Source\A\Sub\deep.txt");
        temp.File(@"Source\B\b1.txt");
        var source = Path.Combine(temp.Path, "Source");
        var destination = temp.Dir("Dest");

        var root = FolderScanner.Scan(source);
        Assert.Equal(4, root.DescendantsAndSelf().Count()); // Source, A, Sub, B

        var modes = Modes(("A", NodeCopyMode.FolderAndFiles)); // A 는 폴더+파일
        var options = new ReplicationOptions { DestinationRoot = destination, CreateRootFolder = true, SkipHidden = true };
        var plan = ReplicationPlanner.Build(root, modes, options);
        var result = ReplicationExecutor.Execute(plan, ConflictPolicy.Skip);

        Assert.False(result.Cancelled);
        Assert.Equal(0, result.Errors);

        var target = Path.Combine(destination, "Source");
        Assert.True(Directory.Exists(Path.Combine(target, "A", "Sub")));
        Assert.True(Directory.Exists(Path.Combine(target, "B")));
        Assert.True(File.Exists(Path.Combine(target, "A", "a1.txt")));      // 선택 폴더의 바로 아래 파일
        Assert.False(File.Exists(Path.Combine(target, "A", "Sub", "deep.txt"))); // 선택 폴더 안의 하위 폴더 파일은 제외
        Assert.False(File.Exists(Path.Combine(target, "B", "b1.txt")));    // 미선택 폴더
        Assert.False(File.Exists(Path.Combine(target, "root.txt")));       // 루트 미선택
    }

    [Fact]
    public void Replicate_KeepBoth_RenamesExistingFile()
    {
        using var temp = new TempFolder();
        temp.File(@"Source\x.txt", "new");
        temp.File(@"Dest\x.txt", "old");
        var root = FolderScanner.Scan(Path.Combine(temp.Path, "Source"));
        var options = new ReplicationOptions { DestinationRoot = Path.Combine(temp.Path, "Dest"), CreateRootFolder = false };
        var plan = ReplicationPlanner.Build(root, Modes((string.Empty, NodeCopyMode.FolderAndFiles)), options);

        ReplicationExecutor.Execute(plan, ConflictPolicy.KeepBoth);

        Assert.Equal("old", File.ReadAllText(Path.Combine(temp.Path, "Dest", "x.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(temp.Path, "Dest", "x (2).txt")));
    }

    [Fact]
    public void Replicate_ExcludedFolder_SkipsItAndItsSubtree()
    {
        using var temp = new TempFolder();
        temp.File(@"Source\A\a1.txt");
        temp.File(@"Source\A\Sub\deep.txt");
        temp.File(@"Source\B\b1.txt");
        var source = Path.Combine(temp.Path, "Source");
        var destination = temp.Dir("Dest");

        var root = FolderScanner.Scan(source);
        // A 를 제외 → A 와 A\Sub 는 생성조차 안 됨. B 는 폴더만 생성(파일 복사 안 함).
        var modes = Modes(("A", NodeCopyMode.Exclude));
        var options = new ReplicationOptions { DestinationRoot = destination, CreateRootFolder = true, SkipHidden = true };
        var plan = ReplicationPlanner.Build(root, modes, options);
        ReplicationExecutor.Execute(plan, ConflictPolicy.Skip);

        var target = Path.Combine(destination, "Source");
        Assert.False(Directory.Exists(Path.Combine(target, "A")));
        Assert.False(Directory.Exists(Path.Combine(target, "A", "Sub")));
        Assert.True(Directory.Exists(Path.Combine(target, "B")));
        Assert.False(File.Exists(Path.Combine(target, "B", "b1.txt")));
    }

    [Fact]
    public void Validate_RejectsDestinationInsideSource()
    {
        using var temp = new TempFolder();
        var source = temp.Dir("Source");
        Assert.NotNull(ReplicationPlanner.Validate(source, Path.Combine(source, "inner")));
        Assert.Null(ReplicationPlanner.Validate(source, temp.Dir("Other")));
    }

    private static Dictionary<string, NodeCopyMode> Modes(params (string Path, NodeCopyMode Mode)[] entries) =>
        entries.ToDictionary(e => e.Path, e => e.Mode, StringComparer.OrdinalIgnoreCase);
}
