using Microsoft.Extensions.DependencyInjection;

namespace SunTool.Core.Modules;

/// <summary>
/// SunTool 기능 모듈 계약.
/// 새 기능을 추가하려면 이 인터페이스를 구현한 클래스(매개변수 없는 생성자)를 만들고,
/// App 프로젝트의 ModuleAssemblies 에 해당 어셈블리를 한 줄 추가하면 됩니다.
/// 화면은 "XxxViewModel" → "XxxView" 이름 규칙으로 자동 연결됩니다
/// (네임스페이스 .ViewModels → .Views).
/// </summary>
public interface IToolModule
{
    /// <summary>고유 ID (설정 키 등에 사용).</summary>
    string Id { get; }

    /// <summary>사이드바에 표시될 이름.</summary>
    string DisplayName { get; }

    /// <summary>화면 상단에 표시될 한 줄 설명.</summary>
    string Description { get; }

    /// <summary>Segoe MDL2 Assets 아이콘 글리프 (예: "").</summary>
    string IconGlyph { get; }

    /// <summary>사이드바 정렬 순서 (작을수록 위).</summary>
    int Order { get; }

    /// <summary>모듈이 쓰는 ViewModel/서비스를 DI 컨테이너에 등록합니다.</summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>모듈의 메인 ViewModel을 만듭니다 (처음 선택될 때 한 번 호출).</summary>
    object CreateViewModel(IServiceProvider services);
}
