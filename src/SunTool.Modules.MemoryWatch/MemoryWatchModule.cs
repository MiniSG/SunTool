using Microsoft.Extensions.DependencyInjection;
using SunTool.Core.Modules;
using SunTool.Modules.MemoryWatch.ViewModels;

namespace SunTool.Modules.MemoryWatch;

/// <summary>기능 6: 다른 프로그램의 메모리·핸들·GDI/USER·스레드 수 감시 (그래프, CSV, 참고용 추세 판정).</summary>
public sealed class MemoryWatchModule : IToolModule
{
    public string Id => "memory-watch";

    public string DisplayName => Loc.T("메모리 감시");

    public string Description => Loc.T("실행 중인 프로그램의 메모리·핸들·GDI/USER·스레드 수를 일정 간격으로 기록해 계속 늘어나는지(메모리릭) 확인합니다.");

    public string IconGlyph => "";

    public int Order => 60;

    public void RegisterServices(IServiceCollection services) => services.AddSingleton<MemoryWatchViewModel>();

    public object CreateViewModel(IServiceProvider services) => services.GetRequiredService<MemoryWatchViewModel>();
}
