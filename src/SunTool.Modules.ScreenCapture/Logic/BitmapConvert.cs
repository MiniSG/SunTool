using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;

namespace SunTool.Modules.ScreenCapture.Logic;

/// <summary>GDI 비트맵을 WPF 이미지로 바꾸기 (미리보기·클립보드용).</summary>
public static class BitmapConvert
{
    /// <summary>System.Drawing.Bitmap → 얼린(freeze) WPF BitmapSource. 다른 스레드에서도 안전합니다.</summary>
    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
