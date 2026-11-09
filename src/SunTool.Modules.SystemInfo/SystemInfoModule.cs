using Microsoft.Extensions.DependencyInjection;
using SunTool.Core.Modules;
using SunTool.Modules.SystemInfo.Logic;
using SunTool.Modules.SystemInfo.Logic.Providers;
using SunTool.Modules.SystemInfo.ViewModels;

namespace SunTool.Modules.SystemInfo;

/// <summary>기능 4: 시스템 정보 (컴퓨터·통신·카메라/그랩보드·이미지 라이브러리·개발 도구).</summary>
public sealed class SystemInfoModule : IToolModule
{
    public string Id => "system-info";

    public string DisplayName => Loc.T("시스템 정보");

    public string Description => Loc.T("CPU·RAM·LAN·추가 COM 포트, 카메라·그랩보드(Basler·DALSA), HALCON, Visual Studio 설치 정보를 한 번에 확인하고 CSV로 저장합니다.");

    public string IconGlyph => "";

    public int Order => 40;

    public void RegisterServices(IServiceCollection services)
    {
        // 분류를 추가하려면 ISystemInfoProvider 구현을 여기에 한 줄 등록
        services.AddSingleton<ISystemInfoProvider, ComputerInfoProvider>();
        services.AddSingleton<ISystemInfoProvider, CommunicationInfoProvider>();
        services.AddSingleton<ISystemInfoProvider, CameraInfoProvider>();
        services.AddSingleton<ISystemInfoProvider, VisionLibraryInfoProvider>();
        services.AddSingleton<ISystemInfoProvider, DevToolsInfoProvider>();
        services.AddSingleton<SystemInfoViewModel>();

        // 터미널(F12) 진단 명령: programs <검색어>
        services.AddSingleton<SunTool.Core.Diagnostics.IConsoleCommand, FindProgramsCommand>();
    }

    public object CreateViewModel(IServiceProvider services) =>
        services.GetRequiredService<SystemInfoViewModel>();
}
