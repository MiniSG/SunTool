using System.Drawing;
using System.Drawing.Imaging;
using static SunTool.Modules.ScreenCapture.Logic.NativeMethods;

namespace SunTool.Modules.ScreenCapture.Logic;

/// <summary>화면·창을 비트맵으로 떠 옵니다. 화면 내용을 읽기만 하고 아무것도 바꾸지 않습니다.</summary>
public static class ScreenCapturer
{
    /// <summary>화면 좌표의 사각형 영역을 캡처합니다.</summary>
    public static Bitmap CaptureRegion(Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentException("빈 영역은 캡처할 수 없습니다.", nameof(region));
        }

        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        }

        return bitmap;
    }

    /// <summary>창 하나를 캡처합니다. 가려져 있어도 PrintWindow 로 그려 받고, 안 되면 화면에서 떠 옵니다.</summary>
    public static Bitmap CaptureWindow(WindowInfo window)
    {
        var bounds = window.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("창 크기를 알 수 없습니다.");
        }

        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        var ok = false;
        using (var g = Graphics.FromImage(bitmap))
        {
            var hdc = g.GetHdc();
            try
            {
                ok = PrintWindow(window.Handle, hdc, PW_RENDERFULLCONTENT);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        if (ok && !IsMostlyBlank(bitmap))
        {
            return bitmap;
        }

        // PrintWindow 가 비거나 실패하면 화면에서 그대로 떠 옵니다 (가려진 부분은 못 담음).
        bitmap.Dispose();
        return CaptureRegion(bounds);
    }

    /// <summary>PrintWindow 결과가 전부 같은 색(대개 검정)인지 대략 확인합니다.</summary>
    private static bool IsMostlyBlank(Bitmap bitmap)
    {
        var first = bitmap.GetPixel(0, 0);
        var xs = new[] { 0, bitmap.Width / 2, bitmap.Width - 1 };
        var ys = new[] { 0, bitmap.Height / 2, bitmap.Height - 1 };
        foreach (var x in xs)
        {
            foreach (var y in ys)
            {
                if (bitmap.GetPixel(x, y) != first)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
