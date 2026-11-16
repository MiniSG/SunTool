using System.Security.Cryptography;

namespace SunTool.Modules.RemoteView.Logic;

/// <summary>접속용 PIN. 호스트가 공유를 켤 때 새로 만들고, 뷰어가 맞게 입력해야 연결됩니다.</summary>
public static class Pin
{
    /// <summary>임의의 6자리 숫자 PIN.</summary>
    public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>시간 일정 비교(타이밍으로 PIN 을 추측하지 못하도록).</summary>
    public static bool Matches(string? expected, string? given)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(expected ?? string.Empty);
        var b = System.Text.Encoding.UTF8.GetBytes(given ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
