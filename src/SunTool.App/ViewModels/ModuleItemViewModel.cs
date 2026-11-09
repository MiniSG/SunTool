using SunTool.Core.Modules;

namespace SunTool.App.ViewModels;

/// <summary>사이드바의 모듈 한 항목. 실제 모듈 ViewModel 은 처음 선택될 때 만들어집니다.</summary>
public sealed class ModuleItemViewModel(IToolModule module, IServiceProvider services)
{
    private object? _viewModel;

    public IToolModule Module { get; } = module;

    public string Id => Module.Id;

    public string DisplayName => Module.DisplayName;

    public string Description => Module.Description;

    public string IconGlyph => Module.IconGlyph;

    public object GetOrCreateViewModel() => _viewModel ??= Module.CreateViewModel(services);
}
