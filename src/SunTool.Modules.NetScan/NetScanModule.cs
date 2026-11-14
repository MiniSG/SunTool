using Microsoft.Extensions.DependencyInjection;
using SunTool.Core.Modules;
using SunTool.Modules.NetScan.ViewModels;

namespace SunTool.Modules.NetScan;

/// <summary>기능 7: 핑으로 같은 네트워크의 살아 있는 장치 찾기 (IP·응답·TTL·MAC·제조사·이름, CSV).</summary>
public sealed class NetScanModule : IToolModule
{
    public string Id => "net-scan";

    public string DisplayName => Loc.T("장치 검색");

    public string Description => Loc.T("같은 네트워크에서 핑에 응답하는 장치를 찾아 IP·MAC·제조사·이름·응답 시간을 보여 줍니다. 포트 검사는 하지 않습니다.");

    public string IconGlyph => "";

    public int Order => 70;

    public void RegisterServices(IServiceCollection services) => services.AddSingleton<NetScanViewModel>();

    public object CreateViewModel(IServiceProvider services) => services.GetRequiredService<NetScanViewModel>();
}
