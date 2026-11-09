using System.Windows.Markup;

namespace SunTool.Wpf.Localization;

/// <summary>
/// XAML 번역: Text="{l:T '원본 폴더'}"  →  영어 모드에서는 "Source folder".
/// xmlns:l="clr-namespace:SunTool.Wpf.Localization;assembly=SunTool.Wpf"
/// 언어는 시작할 때 정해지므로 값을 한 번만 읽습니다.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
