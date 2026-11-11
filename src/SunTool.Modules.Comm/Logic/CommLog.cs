using System.Globalization;
using System.Text;

namespace SunTool.Modules.Comm.Logic;

public enum CommDirection
{
    Sent,
    Received,
    Info,
    Error,

    /// <summary>Steal: 장비 1 → 장비 2 로 오간 데이터.</summary>
    Relay1To2,

    /// <summary>Steal: 장비 2 → 장비 1 로 오간 데이터.</summary>
    Relay2To1,
}

/// <summary>통신 로그 한 줄. 데이터 줄(Sent/Received)은 Data, 알림 줄(Info/Error)은 Message 를 씁니다.</summary>
public sealed class CommLogEntry
{
    private string? _hex;

    public CommLogEntry(int index, DateTime time, double? deltaMs, CommDirection direction, string peer, byte[] data, bool? checkOk = null, string? message = null)
    {
        Index = index;
        Time = time;
        DeltaMs = deltaMs;
        Direction = direction;
        Peer = peer;
        Data = data;
        CheckOk = checkOk;
        Message = message;
    }

    public int Index { get; }

    public DateTime Time { get; }

    /// <summary>바로 앞 데이터 줄과의 간격 (ms). 첫 줄은 null.</summary>
    public double? DeltaMs { get; }

    public CommDirection Direction { get; }

    public string Peer { get; }

    public byte[] Data { get; }

    /// <summary>체크섬/CRC 검사 결과 (검사 안 함 = null).</summary>
    public bool? CheckOk { get; }

    public string? Message { get; }

    public bool IsData => Direction is CommDirection.Sent or CommDirection.Received or CommDirection.Relay1To2 or CommDirection.Relay2To1;

    public string IndexText => Index.ToString("D4", CultureInfo.InvariantCulture);

    public string TimeText => Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public string DeltaText => DeltaMs is { } d ? $"+{d:0} ms" : string.Empty;

    public string DirectionText => Direction switch
    {
        CommDirection.Sent => Loc.T("송신"),
        CommDirection.Received => Loc.T("수신"),
        CommDirection.Relay1To2 => "1 → 2",
        CommDirection.Relay2To1 => "2 → 1",
        CommDirection.Error => Loc.T("오류"),
        _ => Loc.T("알림"),
    };

    public string LengthText => IsData ? Data.Length.ToString(CultureInfo.InvariantCulture) : string.Empty;

    public string HexText => _hex ??= ByteCodec.ToHex(Data);

    public string CheckText => CheckOk switch
    {
        true => "CHK ✓",
        false => "CHK ✗",
        _ => string.Empty,
    };

    /// <summary>검색어가 텍스트, HEX(공백 무시), 상대 이름, 알림 메시지 중 하나에 들어 있는지.</summary>
    public bool Matches(string query, TextEncodingKind encoding)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        query = query.Trim();
        if (Message?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
            || Peer.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IsData)
        {
            return false;
        }

        // 하이라이트와 같은 규칙 (텍스트 일치 또는 바이트 단위 HEX 일치)
        return SearchMatcher.FindBytes(Data, query, encoding) is not null;
    }

    /// <summary>로그 파일 한 줄: "0001 [10:42:15.120] 송신 COM3 (8) 텍스트 | HEX".</summary>
    public string ToLogLine(TextEncodingKind encoding)
    {
        var head = $"{IndexText} [{Time:yyyy-MM-dd HH:mm:ss.fff}] {DirectionText,-2} {Peer}";
        if (!IsData)
        {
            return $"{head} : {Message}";
        }

        var check = CheckOk is null ? string.Empty : $" {CheckText}";
        return $"{head} ({Data.Length}){check} : {ByteCodec.ToDisplayText(Data, encoding)}  |  {HexText}";
    }
}

/// <summary>보낼 바이트 만들기: 본문 + 검사값 + CR/LF.</summary>
public static class SendBuilder
{
    public static byte[] Build(
        string input,
        InputMode mode,
        TextEncodingKind encoding,
        bool escapes,
        CheckKind check,
        CheckByteOrder order,
        bool appendCr,
        bool appendLf)
    {
        var body = mode == InputMode.Hex ? ByteCodec.ParseHex(input) : ByteCodec.EncodeText(input, encoding, escapes);
        var tail = Checksum.Compute(check, body, order);
        var result = new byte[body.Length + tail.Length + (appendCr ? 1 : 0) + (appendLf ? 1 : 0)];
        body.CopyTo(result, 0);
        tail.CopyTo(result, body.Length);
        var i = body.Length + tail.Length;
        if (appendCr)
        {
            result[i++] = 0x0D;
        }

        if (appendLf)
        {
            result[i] = 0x0A;
        }

        return result;
    }
}

/// <summary>로그 파일 저장 (UTF-8 BOM, 메모장에서 바로 열림).</summary>
public static class CommLogWriter
{
    public static void Save(string path, string title, IEnumerable<CommLogEntry> entries, TextEncodingKind encoding)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine($"# SunTool {Loc.T("통신 로그")} — {title}");
        writer.WriteLine($"# {Loc.T("저장 시각")}: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine();
        foreach (var entry in entries)
        {
            writer.WriteLine(entry.ToLogLine(encoding));
        }
    }
}
