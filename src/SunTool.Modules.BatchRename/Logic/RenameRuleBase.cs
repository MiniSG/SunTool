using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SunTool.Core.Settings;
using SunTool.Modules.BatchRename.Logic.Rules;

namespace SunTool.Modules.BatchRename.Logic;

/// <summary>이름 변환 규칙 계약. 규칙은 위에서 아래로 순서대로 적용됩니다.</summary>
public interface IRenameRule
{
    bool IsEnabled { get; }

    /// <summary>현재 이름(기본: 확장자 제외)을 받아 새 이름을 돌려줍니다.</summary>
    string Apply(string name, RenameContext context);
}

/// <summary>
/// 모든 규칙의 기반 클래스. 속성이 바뀌면 화면 요약(Summary)도 갱신되고,
/// JSON 직렬화(프리셋 저장)를 위해 하위 타입을 아래에 등록합니다.
/// 새 규칙을 추가하려면: (1) 이 클래스를 상속 (2) JsonDerivedType 등록 (3) RuleCatalog 에 추가
/// (4) BatchRenameView.xaml 에 편집용 DataTemplate 추가.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$rule")]
[JsonDerivedType(typeof(InsertTextRule), "insert")]
[JsonDerivedType(typeof(NumberingRule), "number")]
[JsonDerivedType(typeof(ReplaceRule), "replace")]
[JsonDerivedType(typeof(RemoveRule), "remove")]
[JsonDerivedType(typeof(CaseRule), "case")]
[JsonDerivedType(typeof(TemplateRule), "template")]
public abstract partial class RenameRuleBase : ObservableObject, IRenameRule
{
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>규칙 종류 이름 (예: "텍스트 삽입").</summary>
    [JsonIgnore]
    public string RuleName => GetRuleName();

    /// <summary>현재 설정을 한 줄로 요약 (목록 표시용).</summary>
    [JsonIgnore]
    public string Summary => BuildSummary();

    public abstract string Apply(string name, RenameContext context);

    public RenameRuleBase Clone()
    {
        var json = JsonSerializer.Serialize<RenameRuleBase>(this, JsonSettingsService.SerializerOptions);
        return JsonSerializer.Deserialize<RenameRuleBase>(json, JsonSettingsService.SerializerOptions)!;
    }

    protected abstract string GetRuleName();

    protected abstract string BuildSummary();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(Summary))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        }
    }
}
