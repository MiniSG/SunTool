using Microsoft.Extensions.DependencyInjection;
using SunTool.Core.Modules;
using SunTool.Modules.Comm.Logic;
using SunTool.Modules.Comm.ViewModels;

namespace SunTool.Modules.Comm;

/// <summary>기능 5: 시리얼(COM) · TCP · UDP 통신 테스트 (보내기/받기, HEX, 체크섬·CRC).</summary>
public sealed class CommModule : IToolModule
{
    public string Id => "comm-tester";

    public string DisplayName => Loc.T("통신 테스트");

    public string Description => Loc.T("시리얼(COM) · TCP · UDP 로 데이터를 보내고 받으며, 텍스트와 HEX 로 확인합니다. COM 포트는 [연결]을 누를 때만 엽니다.");

    public string IconGlyph => "";

    public int Order => 50;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<SerialPortCatalog>();
        services.AddSingleton<CommToolViewModel>();
        services.AddSingleton<SunTool.Core.Diagnostics.IConsoleCommand, CommConsoleCommand>();
    }

    public object CreateViewModel(IServiceProvider services) =>
        services.GetRequiredService<CommToolViewModel>();
}
