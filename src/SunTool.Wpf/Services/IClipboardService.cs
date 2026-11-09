using System.Runtime.InteropServices;
using System.Windows;

namespace SunTool.Wpf.Services;

public interface IClipboardService
{
    bool SetText(string text);
}

public sealed class ClipboardService : IClipboardService
{
    public bool SetText(string text)
    {
        // 다른 프로그램이 클립보드를 잡고 있으면 실패할 수 있어 몇 번 재시도합니다.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }
}
