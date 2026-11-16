using Microsoft.Extensions.DependencyInjection;
using SunTool.Core.Modules;
using SunTool.Modules.ScreenCapture.ViewModels;

namespace SunTool.Modules.ScreenCapture;

/// <summary>기능 8: 화면 캡처 (전체 화면·활성 창·창 선택·영역 드래그 → PNG 저장·클립보드·폴더 열기).</summary>
public sealed class ScreenCaptureModule : IToolModule
{
    public string Id => "screen-capture";

    public string DisplayName => Loc.T("화면 캡처");

    public string Description => Loc.T("전체 화면·활성 창·고른 창·드래그한 영역을 캡처해 PNG 로 저장하거나 클립보드에 복사합니다.");

    public string IconGlyph => ""; // Segoe MDL2 Assets: Camera

    public int Order => 80;

    public void RegisterServices(IServiceCollection services) => services.AddSingleton<ScreenCaptureViewModel>();

    public object CreateViewModel(IServiceProvider services) => services.GetRequiredService<ScreenCaptureViewModel>();
}
