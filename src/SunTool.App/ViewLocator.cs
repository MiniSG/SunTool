using System.Windows;
using System.Windows.Controls;

namespace SunTool.App;

/// <summary>
/// 이름 규칙으로 ViewModel 에 맞는 View 를 찾아 주는 템플릿 선택기.
/// Foo.ViewModels.BarViewModel → Foo.Views.BarView (같은 어셈블리)
/// 모듈은 View 를 따로 등록할 필요가 없습니다.
/// </summary>
public sealed class ViewLocator : DataTemplateSelector
{
    private readonly Dictionary<Type, DataTemplate> _cache = new();

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is null)
        {
            return null;
        }

        var viewModelType = item.GetType();
        if (_cache.TryGetValue(viewModelType, out var cached))
        {
            return cached;
        }

        var viewType = ResolveViewType(viewModelType);
        var factory = new FrameworkElementFactory(viewType ?? typeof(TextBlock));
        if (viewType is null)
        {
            factory.SetValue(TextBlock.TextProperty, $"View 를 찾을 수 없습니다: {viewModelType.FullName}");
            factory.SetValue(FrameworkElement.MarginProperty, new Thickness(16));
        }

        var template = new DataTemplate(viewModelType) { VisualTree = factory };
        template.Seal();
        _cache[viewModelType] = template;
        return template;
    }

    private static Type? ResolveViewType(Type viewModelType)
    {
        var fullName = viewModelType.FullName;
        if (fullName is null || !fullName.EndsWith("ViewModel", StringComparison.Ordinal))
        {
            return null;
        }

        var viewName = fullName[..^"Model".Length].Replace(".ViewModels.", ".Views.", StringComparison.Ordinal);
        var viewType = viewModelType.Assembly.GetType(viewName);
        return viewType is not null && typeof(FrameworkElement).IsAssignableFrom(viewType) ? viewType : null;
    }
}
