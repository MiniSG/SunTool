using SunTool.Core.Logging;

namespace SunTool.Modules.SystemInfo.Logic;

public enum InfoStatus
{
    None,
    Ok,
    Installed,
    NotInstalled,
    Warning,
    Error,
}

/// <summary>정보 한 줄 (항목 이름 · 값 · 상태 배지 · 보조 설명).</summary>
public sealed record InfoItem(string Name, string Value, InfoStatus Status = InfoStatus.None, string? Detail = null)
{
    public string StatusText => Status switch
    {
        InfoStatus.Ok => Loc.T("정상"),
        InfoStatus.Installed => Loc.T("설치됨"),
        InfoStatus.NotInstalled => Loc.T("없음"),
        InfoStatus.Warning => Loc.T("확인 필요"),
        InfoStatus.Error => Loc.T("오류"),
        _ => string.Empty,
    };

    public bool HasStatus => Status != InfoStatus.None;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}

/// <summary>화면의 카드 하나 (예: "CPU", "Visual Studio").</summary>
public sealed class InfoSection(string title, IEnumerable<InfoItem>? items = null, string? note = null)
{
    public string Title { get; } = title;

    public List<InfoItem> Items { get; } = items?.ToList() ?? new List<InfoItem>();

    /// <summary>카드 아래 안내 문구.</summary>
    public string? Note { get; set; } = note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    /// <summary>네트워크 카드일 때 Windows 연결 이름 (예: "이더넷 2"). 있으면 [어댑터 속성 열기] 버튼을 보여 줍니다.</summary>
    public string? AdapterName { get; init; }

    public bool HasAdapter => !string.IsNullOrEmpty(AdapterName);
}

public sealed class ProviderResult
{
    public List<InfoSection> Sections { get; } = new();

    /// <summary>'요약' 화면에 보여줄 핵심 항목.</summary>
    public List<InfoItem> Summary { get; } = new();
}

/// <summary>
/// 시스템 정보 분류 하나를 수집합니다. 새 분류를 추가하려면 이 인터페이스를 구현하고
/// SystemInfoModule.RegisterServices 에 등록하면 됩니다.
/// </summary>
public interface ISystemInfoProvider
{
    string Id { get; }

    string Title { get; }

    /// <summary>Segoe MDL2 Assets 글리프.</summary>
    string IconGlyph { get; }

    int Order { get; }

    /// <summary>백그라운드 스레드에서 호출됩니다.</summary>
    ProviderResult Collect(CollectionContext context, CancellationToken cancellationToken);
}

/// <summary>한 번 새로고침하는 동안 여러 수집기가 함께 쓰는 자료 (느린 조회는 한 번만 실행).</summary>
public sealed class CollectionContext
{
    private readonly Lazy<IReadOnlyList<InstalledProgram>> _programs;
    private readonly Lazy<IReadOnlyList<PnpDevice>> _devices;

    public CollectionContext(SoftwareCatalog software, IAppLogger? log = null)
    {
        Software = software;
        Log = log ?? NullLogger.Instance;
        _programs = new Lazy<IReadOnlyList<InstalledProgram>>(() => InstalledProgramReader.ReadAll(Log));
        _devices = new Lazy<IReadOnlyList<PnpDevice>>(() => PnpDeviceReader.ReadAll(Log));
    }

    public SoftwareCatalog Software { get; }

    public IAppLogger Log { get; }

    /// <summary>제어판 '프로그램 및 기능' 목록.</summary>
    public IReadOnlyList<InstalledProgram> InstalledPrograms => _programs.Value;

    /// <summary>장치 관리자 장치 목록.</summary>
    public IReadOnlyList<PnpDevice> PnpDevices => _devices.Value;
}
