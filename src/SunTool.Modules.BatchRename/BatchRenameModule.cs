using Microsoft.Extensions.DependencyInjection;
using SunTool.Core.Modules;
using SunTool.Modules.BatchRename.ViewModels;

namespace SunTool.Modules.BatchRename;

/// <summary>기능 2: 파일 이름 일괄 변환 (텍스트 삽입, 번호 매기기, 치환 등 규칙 조합).</summary>
public sealed class BatchRenameModule : IToolModule
{
    public string Id => "batch-rename";

    public string DisplayName => Loc.T("파일 이름 일괄 변경");

    public string Description => Loc.T("폴더 안 파일들의 이름 앞/중간/끝에 텍스트를 넣거나 원하는 형식으로 번호를 매깁니다. 미리보기 후 실행, 되돌리기 지원.");

    public string IconGlyph => "";

    public int Order => 20;

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<BatchRenameViewModel>();

    public object CreateViewModel(IServiceProvider services) =>
        services.GetRequiredService<BatchRenameViewModel>();
}
