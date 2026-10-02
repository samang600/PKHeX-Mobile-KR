using System;

namespace PKHeX.Core.Injection;

public static class Decoder
{

    public static byte[] ConvertHexByteStringToBytes(ReadOnlySpan<byte> bytes)
    {
        var dest = new byte[bytes.Length / 2];
        ConvertHexByteStringToBytes(bytes, dest);
        return dest;
    }

    public static void ConvertHexByteStringToBytes(ReadOnlySpan<byte> bytes, Span<byte> dest)
    {
        Convert.FromHexString(bytes, dest, out _, out _);
    }

    public static byte[] StringToByteArray(ReadOnlySpan<char> hex) => Convert.FromHexString(hex);

}
