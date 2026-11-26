using System.IO;
using System.Reflection;
using SunTool.Core;

namespace SunTool.App.Services;

/// <summary>
/// exe 에 포함된 매뉴얼 PDF 를 쓰기 가능한 데이터 폴더로 꺼내 줍니다(포터블 단일 exe 대응).
/// </summary>
public static class ManualFile
{
    /// <summary>EmbeddedResource 의 LogicalName 과 꺼낼 파일 이름(영문 — 일부 뷰어의 한글 경로 문제 회피).</summary>
    public const string ResourceName = "SunTool-Manual.pdf";

    /// <summary>매뉴얼 PDF 를 데이터 폴더로 꺼내고 그 전체 경로를 돌려줍니다.</summary>
    public static string Extract()
    {
        var target = Path.Combine(AppPaths.DataDirectory, ResourceName);

        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                ?? throw new FileNotFoundException("매뉴얼 리소스를 찾을 수 없습니다.", ResourceName);
            using var file = File.Create(target);
            stream.CopyTo(file);
        }
        catch (IOException) when (File.Exists(target))
        {
            // 이미 열려 있어 덮어쓸 수 없으면 기존에 꺼내 둔 파일을 그대로 씁니다.
        }

        return target;
    }
}
