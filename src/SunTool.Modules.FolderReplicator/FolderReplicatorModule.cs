using Microsoft.Extensions.DependencyInjection;
using SunTool.Core.Modules;
using SunTool.Modules.FolderReplicator.ViewModels;

namespace SunTool.Modules.FolderReplicator;

/// <summary>기능 1: 폴더 트리 구조 복제 + 선택 폴더의 바로 아래 파일 복사.</summary>
public sealed class FolderReplicatorModule : IToolModule
{
    public string Id => "folder-replicator";

    public string DisplayName => Loc.T("폴더 구조 복제");

    public string Description => Loc.T("폴더 트리를 만들고, 폴더마다 복사 방식을 고릅니다: 폴더만 · 폴더+파일 · 제외.");

    public string IconGlyph => "";

    public int Order => 10;

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<FolderReplicatorViewModel>();

    public object CreateViewModel(IServiceProvider services) =>
        services.GetRequiredService<FolderReplicatorViewModel>();
}
