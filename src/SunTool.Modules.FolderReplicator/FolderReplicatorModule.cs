using Microsoft.Extensions.DependencyInjection;
using SunTool.Core.Modules;
using SunTool.Modules.FolderReplicator.ViewModels;

namespace SunTool.Modules.FolderReplicator;

/// <summary>기능 1: 폴더 트리 구조 복제 + 선택 폴더의 바로 아래 파일 복사.</summary>
public sealed class FolderReplicatorModule : IToolModule
{
    public string Id => "folder-replicator";

    public string DisplayName => "폴더 구조 복제";

    public string Description => "폴더 트리를 그대로 만들고, 체크한 폴더는 바로 아래 파일만 복사합니다 (그 안의 하위 폴더 내용은 제외).";

    public string IconGlyph => "";

    public int Order => 10;

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<FolderReplicatorViewModel>();

    public object CreateViewModel(IServiceProvider services) =>
        services.GetRequiredService<FolderReplicatorViewModel>();
}
