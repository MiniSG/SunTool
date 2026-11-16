namespace SunTool.Modules.RemoteView.Logic;

/// <summary>
/// 뷰어 입력 좌표 매핑 (순수 함수 — 단위 테스트 대상).
/// 표시 좌표 → (레터박스 제거) 표시 이미지 0~1 → (모니터 크롭 보정) 전체 전송 이미지 0~1.
/// 전체 이미지는 호스트 가상 화면의 균일 축소본이므로, 이 정규화 값을 그대로 호스트가 절대좌표로 씁니다.
/// </summary>
public static class InputMapping
{
    /// <summary>
    /// Stretch=Uniform 로 그린 이미지 위의 표시 좌표(px,py)를 콘텐츠 영역 기준 0~1(u,v)로 바꿉니다.
    /// 레터박스(여백) 바깥이면 Inside=false (값은 0~1 로 자름).
    /// </summary>
    public static (double U, double V, bool Inside) DisplayToUnit(
        double px, double py, double actualW, double actualH, int srcW, int srcH)
    {
        if (actualW <= 0 || actualH <= 0 || srcW <= 0 || srcH <= 0)
        {
            return (0, 0, false);
        }

        var scale = Math.Min(actualW / srcW, actualH / srcH);
        var contentW = srcW * scale;
        var contentH = srcH * scale;
        var offsetX = (actualW - contentW) / 2;
        var offsetY = (actualH - contentH) / 2;
        var u = (px - offsetX) / contentW;
        var v = (py - offsetY) / contentH;
        var inside = u is >= 0 and <= 1 && v is >= 0 and <= 1;
        return (Math.Clamp(u, 0, 1), Math.Clamp(v, 0, 1), inside);
    }

    /// <summary>
    /// 표시 이미지 기준 0~1(u,v)을 전체 전송 이미지 기준 0~1(nx,ny)로 바꿉니다.
    /// 개별 모니터를 보는 중이면 그 크롭 영역(전체 이미지 픽셀 좌표) 안으로 환산합니다.
    /// crop 폭/높이가 0 이하이면 전체 이미지를 보는 것으로 봅니다.
    /// </summary>
    public static (float Nx, float Ny) ToFull(
        double u, double v, int cropX, int cropY, int cropW, int cropH, int fullW, int fullH)
    {
        if (fullW <= 0 || fullH <= 0)
        {
            return (0, 0);
        }

        if (cropW <= 0 || cropH <= 0)
        {
            cropX = 0;
            cropY = 0;
            cropW = fullW;
            cropH = fullH;
        }

        var nx = (cropX + u * cropW) / fullW;
        var ny = (cropY + v * cropH) / fullH;
        return ((float)Math.Clamp(nx, 0, 1), (float)Math.Clamp(ny, 0, 1));
    }
}
