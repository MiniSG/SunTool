using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SunTool.Modules.Comm.Logic;
using SunTool.Modules.Comm.ViewModels;

namespace SunTool.Modules.Comm.Views;

/// <summary>
/// TextBlock 에 붙여서 바이트를 읽기 쉽게 그리는 속성 (v:ByteText.Bytes="{Binding Data}", 배열 형식 속성은 템플릿에서 쓸 수 없어 object 로 받음).
/// 텍스트: 제어 문자를 ⟨STX⟩ 처럼 색으로 강조. HEX: 8바이트마다 한 칸 더 띄우고 제어 문자 바이트를 같은 색으로 강조.
/// 둘 다: 텍스트 줄 아래에 HEX 줄을 작게.
/// Query 가 있으면 일치하는 바이트를 텍스트 줄과 HEX 줄 양쪽에서 하이라이트합니다.
/// </summary>
public static class ByteText
{
    /// <summary>한 줄에 그릴 최대 바이트 (넘으면 줄임, 전체는 아래 덤프에서 확인).</summary>
    private const int MaxRenderBytes = 1024;

    public static readonly DependencyProperty BytesProperty = DependencyProperty.RegisterAttached(
        "Bytes", typeof(object), typeof(ByteText), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode", typeof(LogViewMode), typeof(ByteText), new PropertyMetadata(LogViewMode.Both, OnChanged));

    public static readonly DependencyProperty EncodingProperty = DependencyProperty.RegisterAttached(
        "Encoding", typeof(TextEncodingKind), typeof(ByteText), new PropertyMetadata(TextEncodingKind.Ascii, OnChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(ByteText), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty ControlBrushProperty = DependencyProperty.RegisterAttached(
        "ControlBrush", typeof(Brush), typeof(ByteText), new PropertyMetadata(Brushes.DarkOrange, OnChanged));

    public static readonly DependencyProperty SecondaryBrushProperty = DependencyProperty.RegisterAttached(
        "SecondaryBrush", typeof(Brush), typeof(ByteText), new PropertyMetadata(Brushes.Gray, OnChanged));

    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.RegisterAttached(
        "HighlightBrush", typeof(Brush), typeof(ByteText), new PropertyMetadata(Brushes.Yellow, OnChanged));

    public static object? GetBytes(DependencyObject o) => o.GetValue(BytesProperty);

    public static void SetBytes(DependencyObject o, object? value) => o.SetValue(BytesProperty, value);

    public static LogViewMode GetMode(DependencyObject o) => (LogViewMode)o.GetValue(ModeProperty);

    public static void SetMode(DependencyObject o, LogViewMode value) => o.SetValue(ModeProperty, value);

    public static TextEncodingKind GetEncoding(DependencyObject o) => (TextEncodingKind)o.GetValue(EncodingProperty);

    public static void SetEncoding(DependencyObject o, TextEncodingKind value) => o.SetValue(EncodingProperty, value);

    public static string? GetQuery(DependencyObject o) => (string?)o.GetValue(QueryProperty);

    public static void SetQuery(DependencyObject o, string? value) => o.SetValue(QueryProperty, value);

    public static Brush GetControlBrush(DependencyObject o) => (Brush)o.GetValue(ControlBrushProperty);

    public static void SetControlBrush(DependencyObject o, Brush value) => o.SetValue(ControlBrushProperty, value);

    public static Brush GetSecondaryBrush(DependencyObject o) => (Brush)o.GetValue(SecondaryBrushProperty);

    public static void SetSecondaryBrush(DependencyObject o, Brush value) => o.SetValue(SecondaryBrushProperty, value);

    public static Brush GetHighlightBrush(DependencyObject o) => (Brush)o.GetValue(HighlightBrushProperty);

    public static void SetHighlightBrush(DependencyObject o, Brush value) => o.SetValue(HighlightBrushProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock text)
        {
            Rebuild(text);
        }
    }

    private static void Rebuild(TextBlock target)
    {
        target.Inlines.Clear();
        if (GetBytes(target) is not byte[] data || data.Length == 0)
        {
            return;
        }

        var shown = data.AsSpan(0, Math.Min(data.Length, MaxRenderBytes));
        var style = new RenderStyle(
            GetControlBrush(target),
            GetSecondaryBrush(target),
            GetHighlightBrush(target),
            Math.Max(9, target.FontSize - 1.5),
            SearchMatcher.FindBytes(shown, GetQuery(target), GetEncoding(target)));

        switch (GetMode(target))
        {
            case LogViewMode.Text:
                AddText(target, shown, style);
                break;
            case LogViewMode.Hex:
                AddHex(target, shown, style, normal: null, target.FontSize);
                break;
            default:
                AddText(target, shown, style);
                target.Inlines.Add(new LineBreak());
                AddHex(target, shown, style, style.Secondary, style.SmallSize);
                break;
        }

        if (data.Length > MaxRenderBytes)
        {
            target.Inlines.Add(new Run(Loc.F("  … (+{0} 바이트, 아래 덤프에서 전체 확인)", data.Length - MaxRenderBytes)) { Foreground = style.Secondary });
        }
    }

    private sealed record RenderStyle(Brush Control, Brush Secondary, Brush Highlight, double SmallSize, bool[]? Mask);

    /// <summary>텍스트 줄: 글자를 (제어 문자 여부, 하이라이트 여부)가 같은 묶음으로 나눠 Run 으로 그립니다.</summary>
    private static void AddText(TextBlock target, ReadOnlySpan<byte> data, RenderStyle style)
    {
        var map = DisplayMap.Build(data, GetEncoding(target));
        var sb = new StringBuilder();
        var runControl = false;
        var runHighlight = false;

        void Flush()
        {
            if (sb.Length == 0)
            {
                return;
            }

            var run = new Run(sb.ToString());
            if (runControl)
            {
                run.Foreground = style.Control;
                run.FontSize = style.SmallSize;
            }

            if (runHighlight)
            {
                run.Background = style.Highlight;
            }

            target.Inlines.Add(run);
            sb.Clear();
        }

        for (var i = 0; i < map.Text.Length; i++)
        {
            var isControl = map.IsControl[i];
            var isHighlight = style.Mask?[map.ByteIndex[i]] == true;
            if (sb.Length > 0 && (isControl != runControl || isHighlight != runHighlight))
            {
                Flush();
            }

            runControl = isControl;
            runHighlight = isHighlight;
            var c = map.Text[i];
            // 제어 문자 이름의 꺾쇠는 ⟨ ⟩ 로 표시 (실제 데이터의 < > 와 구분)
            sb.Append(isControl && c == '<' ? '⟨' : isControl && c == '>' ? '⟩' : c);
        }

        Flush();
    }

    /// <summary>HEX 줄: 제어 문자 바이트는 색, 일치하는 바이트는 배경(사이 공백까지 이어서 칠함).</summary>
    private static void AddHex(TextBlock target, ReadOnlySpan<byte> data, RenderStyle style, Brush? normal, double size)
    {
        // 같은 색·배경이 이어지는 글자는 Run 하나로 묶음
        var sb = new StringBuilder();
        Brush? runForeground = null;
        Brush? runBackground = null;

        void Append(string text, Brush? foreground, Brush? background)
        {
            if (sb.Length > 0 && (!ReferenceEquals(foreground, runForeground) || !ReferenceEquals(background, runBackground)))
            {
                target.Inlines.Add(MakeRun(sb.ToString(), runForeground, size, runBackground));
                sb.Clear();
            }

            runForeground = foreground;
            runBackground = background;
            sb.Append(text);
        }

        for (var i = 0; i < data.Length; i++)
        {
            var highlighted = style.Mask?[i] == true;
            if (i > 0)
            {
                // 이어서 일치하는 바이트 사이의 공백도 칠해서 한 덩어리로 보이게
                var joined = highlighted && style.Mask![i - 1];
                Append(i % 8 == 0 ? "  " : " ", normal, joined ? style.Highlight : null);
            }

            var foreground = ByteCodec.ControlName(data[i]) is not null ? style.Control : normal;
            Append(data[i].ToString("X2"), foreground, highlighted ? style.Highlight : null);
        }

        if (sb.Length > 0)
        {
            target.Inlines.Add(MakeRun(sb.ToString(), runForeground, size, runBackground));
        }
    }

    private static Run MakeRun(string text, Brush? foreground, double size, Brush? background)
    {
        var run = new Run(text) { FontSize = size };
        if (foreground is not null)
        {
            run.Foreground = foreground;
        }

        if (background is not null)
        {
            run.Background = background;
        }

        return run;
    }
}

/// <summary>일반 글자에서 검색어와 일치하는 부분을 하이라이트 (v:HighlightText.Text, v:HighlightText.Query).</summary>
public static class HighlightText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(HighlightText), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(HighlightText), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.RegisterAttached(
        "Brush", typeof(Brush), typeof(HighlightText), new PropertyMetadata(Brushes.Yellow, OnChanged));

    public static string? GetText(DependencyObject o) => (string?)o.GetValue(TextProperty);

    public static void SetText(DependencyObject o, string? value) => o.SetValue(TextProperty, value);

    public static string? GetQuery(DependencyObject o) => (string?)o.GetValue(QueryProperty);

    public static void SetQuery(DependencyObject o, string? value) => o.SetValue(QueryProperty, value);

    public static Brush GetBrush(DependencyObject o) => (Brush)o.GetValue(BrushProperty);

    public static void SetBrush(DependencyObject o, Brush value) => o.SetValue(BrushProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock target)
        {
            return;
        }

        target.Inlines.Clear();
        var text = GetText(target) ?? string.Empty;
        var pos = 0;
        foreach (var (start, length) in SearchMatcher.FindText(text, GetQuery(target)))
        {
            if (start > pos)
            {
                target.Inlines.Add(new Run(text[pos..start]));
            }

            target.Inlines.Add(new Run(text.Substring(start, length)) { Background = GetBrush(target) });
            pos = start + length;
        }

        if (pos < text.Length)
        {
            target.Inlines.Add(new Run(text[pos..]));
        }
    }
}
