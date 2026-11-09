using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SunTool.Modules.EventLogViewer.Logic;

/// <summary>
/// 예전 방식(클래식) 이벤트 원본의 메시지 파일(DLL/EXE)에 들어 있는 메시지 테이블(RT_MESSAGETABLE)을 읽습니다.
/// Windows 리소스 로더를 쓰므로 OS 언어(한국어 MUI)의 문장이 나옵니다.
/// </summary>
public static class MessageTableReader
{
    private const uint LoadLibraryAsDatafile = 0x00000002;
    private const uint LoadLibraryAsImageResource = 0x00000020;
    private static readonly IntPtr RtMessageTable = 11;
    private static readonly Regex EscapeRegex = new(@"%([0nrtb.!%])", RegexOptions.Compiled);

    static MessageTableReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>메시지 ID(32비트, 심각도 비트 포함)와 원문 목록. 읽을 수 없으면 빈 목록.</summary>
    public static IReadOnlyList<(uint MessageId, string Text)> Read(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (expanded.Length == 0 || !File.Exists(expanded))
        {
            return Array.Empty<(uint, string)>();
        }

        var module = LoadLibraryEx(expanded, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
        if (module == IntPtr.Zero)
        {
            return Array.Empty<(uint, string)>();
        }

        try
        {
            var info = FindResource(module, 1, RtMessageTable);
            if (info == IntPtr.Zero)
            {
                return Array.Empty<(uint, string)>();
            }

            var size = (int)SizeofResource(module, info);
            var handle = LoadResource(module, info);
            var pointer = handle == IntPtr.Zero ? IntPtr.Zero : LockResource(handle);
            if (pointer == IntPtr.Zero || size <= 4)
            {
                return Array.Empty<(uint, string)>();
            }

            var data = new byte[size];
            Marshal.Copy(pointer, data, 0, size);
            return Parse(data);
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    /// <summary>MESSAGE_RESOURCE_DATA 구조를 해석합니다.</summary>
    public static List<(uint MessageId, string Text)> Parse(ReadOnlySpan<byte> data)
    {
        var result = new List<(uint, string)>();
        var ansi = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        var blockCount = BinaryPrimitives.ReadUInt32LittleEndian(data);

        for (var b = 0; b < blockCount; b++)
        {
            var blockOffset = 4 + (b * 12);
            if (blockOffset + 12 > data.Length)
            {
                break;
            }

            var lowId = BinaryPrimitives.ReadUInt32LittleEndian(data[blockOffset..]);
            var highId = BinaryPrimitives.ReadUInt32LittleEndian(data[(blockOffset + 4)..]);
            var position = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(blockOffset + 8)..]);

            for (var id = lowId; id <= highId && id >= lowId; id++)
            {
                if (position + 4 > data.Length)
                {
                    break;
                }

                var length = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
                var flags = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 2)..]);
                if (length < 4 || position + length > data.Length)
                {
                    break;
                }

                var bytes = data.Slice(position + 4, length - 4);
                var text = (flags & 1) == 1 ? Encoding.Unicode.GetString(bytes) : ansi.GetString(bytes);
                result.Add((id, text.TrimEnd('\0')));
                position += length;

                if (id == uint.MaxValue)
                {
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>%n(줄바꿈), %0(끝) 같은 메시지 이스케이프를 읽기 좋은 문자로 바꿉니다. %1 같은 빈칸은 유지합니다.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var cleaned = EscapeRegex.Replace(text, m => m.Groups[1].Value switch
        {
            "0" => string.Empty,
            "n" => "\n",
            "r" => string.Empty,
            "t" => "\t",
            "b" => " ",
            _ => m.Groups[1].Value, // . ! %
        });

        return cleaned.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resourceInfo);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr resourceData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resourceInfo);
}
