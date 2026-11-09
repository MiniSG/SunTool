namespace SunTool.Modules.SystemInfo.Logic.Providers;

/// <summary>이미지 라이브러리: HALCON(버전) 등 정의 파일의 vision 항목.</summary>
public sealed class VisionLibraryInfoProvider : ISystemInfoProvider
{
    public string Id => "vision";

    public string Title => "이미지 라이브러리";

    public string IconGlyph => "";

    public int Order => 40;

    public ProviderResult Collect(CollectionContext context, CancellationToken cancellationToken)
    {
        var result = new ProviderResult();
        var section = new InfoSection("이미지 처리 라이브러리")
        {
            Note = "HALCON 은 '프로그램 및 기능' 버전, HALCONROOT 환경 변수, halcon.dll 파일 버전으로 확인합니다. 여러 버전이 설치되어 있으면 각각 표시합니다.",
        };

        foreach (var definition in context.Software.ForCategory("vision"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = SoftwareDetector.ToItems(SoftwareDetector.Detect(definition, context.InstalledPrograms));
            section.Items.AddRange(items);
            if (definition.Summary)
            {
                result.Summary.AddRange(items.Select(i => i with { Detail = null }));
            }
        }

        result.Sections.Add(section);
        return result;
    }
}
