using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SunTool.Modules.RemoteView.Logic;

/// <summary>호스트 화면을 떠서 JPEG 로 압축합니다. 화면을 읽기만 합니다.</summary>
public sealed class ScreenSource : IDisposable
{
    private readonly ImageCodecInfo _jpegCodec = GetJpegCodec();
    private readonly long _quality;
    private readonly int _maxWidth;

    /// <param name="quality">JPEG 품질 1~100 (높을수록 선명·용량 증가).</param>
    /// <param name="maxWidth">이 너비를 넘으면 비율을 지켜 줄입니다 (0 이면 원본).</param>
    public ScreenSource(int quality = 60, int maxWidth = 1920)
    {
        _quality = Math.Clamp(quality, 1, 100);
        _maxWidth = maxWidth;
    }

    /// <summary>한 모니터 영역 (보낼 이미지 좌표, 픽셀).</summary>
    public readonly record struct MonitorRect(int X, int Y, int Width, int Height);

    /// <summary>보낼 프레임 크기와 그 안에서 각 모니터가 차지하는 영역.</summary>
    public readonly record struct Layout(int Width, int Height, IReadOnlyList<MonitorRect> Monitors);

    /// <summary>가상 화면(모든 모니터) 영역, 픽셀.</summary>
    public static Rectangle VirtualScreen() => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN)),
        Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN)));

    /// <summary>보낼 프레임 크기와, 그 좌표계로 옮긴 각 모니터 영역.</summary>
    public Layout GetLayout()
    {
        var vs = VirtualScreen();
        var (sentW, sentH, scale) = Scaled(vs);
        var monitors = EnumMonitors()
            .Select(m => new MonitorRect(
                (int)Math.Round((m.Left - vs.Left) * scale),
                (int)Math.Round((m.Top - vs.Top) * scale),
                Math.Max(1, (int)Math.Round(m.Width * scale)),
                Math.Max(1, (int)Math.Round(m.Height * scale))))
            .ToList();
        return new Layout(sentW, sentH, monitors);
    }

    /// <summary>지금 화면(모든 모니터)을 떠서 JPEG 바이트로 돌려줍니다.</summary>
    public (byte[] Jpeg, int Width, int Height) Capture()
    {
        var vs = VirtualScreen();
        var (sentW, sentH, _) = Scaled(vs);

        using var raw = new Bitmap(vs.Width, vs.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(raw))
        {
            g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
        }

        Bitmap toEncode = raw;
        Bitmap? scaled = null;
        if (sentW != vs.Width || sentH != vs.Height)
        {
            scaled = new Bitmap(sentW, sentH, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(raw, 0, 0, sentW, sentH);
            }

            toEncode = scaled;
        }

        try
        {
            using var ms = new MemoryStream();
            using var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(Encoder.Quality, _quality);
            toEncode.Save(ms, _jpegCodec, p);
            return (ms.ToArray(), toEncode.Width, toEncode.Height);
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    public void Dispose()
    {
    }

    private (int Width, int Height, double Scale) Scaled(Rectangle vs)
    {
        if (_maxWidth > 0 && vs.Width > _maxWidth)
        {
            var scale = _maxWidth / (double)vs.Width;
            return (_maxWidth, Math.Max(1, (int)Math.Round(vs.Height * scale)), scale);
        }

        return (vs.Width, vs.Height, 1.0);
    }

    private static List<Rectangle> EnumMonitors()
    {
        var list = new List<Rectangle>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref RECT r, IntPtr _) =>
        {
            list.Add(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static ImageCodecInfo GetJpegCodec() =>
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
}
