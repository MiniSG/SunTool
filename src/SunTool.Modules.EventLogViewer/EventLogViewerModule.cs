using Microsoft.Extensions.DependencyInjection;
using SunTool.Core;
using SunTool.Core.Logging;
using SunTool.Core.Modules;
using SunTool.Modules.EventLogViewer.Logic;
using SunTool.Modules.EventLogViewer.ViewModels;

namespace SunTool.Modules.EventLogViewer;

/// <summary>기능 3: Windows 이벤트 뷰어 로그 중 위험·오류·경고 등을 기간으로 골라 조회.</summary>
public sealed class EventLogViewerModule : IToolModule
{
    public string Id => "event-log-viewer";

    public string DisplayName => Loc.T("이벤트 로그 조회");

    public string Description => Loc.T("Windows 이벤트 뷰어의 위험·오류·경고 로그를 기간으로 모아 보고, 이벤트 ID의 뜻을 Windows 내부 정의에서 찾습니다.");

    public string IconGlyph => "";

    public int Order => 30;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(sp => new EventDefinitionCatalog(
            Path.Combine(AppPaths.DataDirectory, "cache"),
            sp.GetRequiredService<ILogService>().For("EventCatalog")));
        services.AddSingleton<EventIdLookupViewModel>();
        services.AddSingleton<EventLogViewerViewModel>();
    }

    public object CreateViewModel(IServiceProvider services) =>
        services.GetRequiredService<EventLogViewerViewModel>();
}
