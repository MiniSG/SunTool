namespace SunTool.Modules.Comm.Logic;

/// <summary>보낼 때 붙이고, 받을 때 검사하는 오류 검사 방식.</summary>
public enum CheckKind
{
    None,

    /// <summary>모든 바이트 합의 하위 8비트 (1바이트).</summary>
    Sum8,

    /// <summary>모든 바이트 XOR (BCC, 1바이트).</summary>
    Xor8,

    /// <summary>CRC-16/MODBUS: 다항식 0xA001(반전), 초기값 0xFFFF (2바이트).</summary>
    Crc16Modbus,

    /// <summary>CRC-16/CCITT-FALSE: 다항식 0x1021, 초기값 0xFFFF (2바이트).</summary>
    Crc16Ccitt,
}

/// <summary>2바이트 검사값을 붙이는 순서.</summary>
public enum CheckByteOrder
{
    /// <summary>상위 바이트 먼저 (HI, LO).</summary>
    HighFirst,

    /// <summary>하위 바이트 먼저 (LO, HI) — Modbus RTU 기본.</summary>
    LowFirst,
}

public static class Checksum
{
    public static int Length(CheckKind kind) => kind switch
    {
        CheckKind.None => 0,
        CheckKind.Sum8 or CheckKind.Xor8 => 1,
        _ => 2,
    };

    public static byte Sum8(ReadOnlySpan<byte> data)
    {
        var sum = 0;
        foreach (var b in data)
        {
            sum += b;
        }

        return (byte)sum;
    }

    public static byte Xor8(ReadOnlySpan<byte> data)
    {
        byte x = 0;
        foreach (var b in data)
        {
            x ^= b;
        }

        return x;
    }

    public static ushort Crc16Modbus(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    public static ushort Crc16Ccitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
        }

        return crc;
    }

    /// <summary>검사값 바이트 (붙일 순서대로).</summary>
    public static byte[] Compute(CheckKind kind, ReadOnlySpan<byte> data, CheckByteOrder order)
    {
        switch (kind)
        {
            case CheckKind.Sum8:
                return [Sum8(data)];
            case CheckKind.Xor8:
                return [Xor8(data)];
            case CheckKind.Crc16Modbus:
            case CheckKind.Crc16Ccitt:
                var crc = kind == CheckKind.Crc16Modbus ? Crc16Modbus(data) : Crc16Ccitt(data);
                var hi = (byte)(crc >> 8);
                var lo = (byte)crc;
                return order == CheckByteOrder.HighFirst ? [hi, lo] : [lo, hi];
            default:
                return [];
        }
    }

    /// <summary>
    /// 받은 데이터 검사: 마지막 검사값 바이트가 앞부분으로 계산한 값과 같은지.
    /// 끝에 CR/LF 가 붙어 있으면 그것을 뺀 위치도 확인합니다 (검사값 자체가 0x0D/0x0A 인 경우 대비).
    /// 데이터가 너무 짧으면 null (판단 안 함).
    /// </summary>
    public static bool? Verify(CheckKind kind, ReadOnlySpan<byte> data, CheckByteOrder order)
    {
        var length = Length(kind);
        if (length == 0)
        {
            return null;
        }

        var stripped = data.Length;
        while (stripped > 0 && data[stripped - 1] is 0x0D or 0x0A)
        {
            stripped--;
        }

        bool? result = null;
        int[] ends = stripped == data.Length ? [data.Length] : [stripped, data.Length];
        foreach (var end in ends)
        {
            if (end <= length)
            {
                continue;
            }

            if (data[(end - length)..end].SequenceEqual(Compute(kind, data[..(end - length)], order)))
            {
                return true;
            }

            result = false;
        }

        return result;
    }
}
