using Microsoft.Extensions.DependencyInjection;
using SunTool.Core.Modules;
using SunTool.Modules.RemoteView.ViewModels;

namespace SunTool.Modules.RemoteView;

/// <summary>기능 9: 원격 화면 보기 (같은 네트워크의 다른 SunTool 화면을 PIN 으로 접속해 실시간으로 봅니다. 보기 전용).</summary>
public sealed class RemoteViewModule : IToolModule
{
    public string Id => "remote-view";

    public string DisplayName => Loc.T("원격 화면 보기");

    public string Description => Loc.T("같은 네트워크의 다른 PC(SunTool)가 공유한 화면을 PIN 으로 접속해 실시간으로 봅니다. 보기 전용이며, 공유하는 쪽이 직접 켜야 합니다.");

    public string IconGlyph => ""; // Segoe MDL2 Assets: Remote

    public int Order => 90;

    public void RegisterServices(IServiceCollection services) => services.AddSingleton<RemoteViewViewModel>();

    public object CreateViewModel(IServiceProvider services) => services.GetRequiredService<RemoteViewViewModel>();
}
