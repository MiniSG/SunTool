using System.Reflection;
using SunTool.Modules.BatchRename;
using SunTool.Modules.Comm;
using SunTool.Modules.EventLogViewer;
using SunTool.Modules.FolderReplicator;
using SunTool.Modules.MemoryWatch;
using SunTool.Modules.SystemInfo;

namespace SunTool.App;

/// <summary>
/// 내장 모듈 어셈블리 목록.
/// 새 모듈 프로젝트를 추가하면: (1) App 프로젝트에서 참조 추가 (2) 아래 배열에 한 줄 추가.
/// 각 어셈블리 안의 IToolModule 구현체는 ModuleCatalog 가 자동으로 찾습니다.
/// </summary>
internal static class ModuleAssemblies
{
    public static Assembly[] All { get; } =
    [
        typeof(FolderReplicatorModule).Assembly,
        typeof(BatchRenameModule).Assembly,
        typeof(EventLogViewerModule).Assembly,
        typeof(SystemInfoModule).Assembly,
        typeof(CommModule).Assembly,
        typeof(MemoryWatchModule).Assembly,
    ];
}
