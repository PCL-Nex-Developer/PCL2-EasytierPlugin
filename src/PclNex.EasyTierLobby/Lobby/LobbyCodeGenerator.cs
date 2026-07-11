using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PclNex.EasyTierLobby.Lobby;

internal static class LobbyCodeGenerator
{
    private const string Chars = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string FullCodePrefix = "U/";
    private const string NetworkNamePrefix = "scaffolding-mc-";
    private const int BaseVal = 34;
    private const int DataLength = 16;
    private const int HyphenCount = 3;
    private const int PayloadLength = DataLength + HyphenCount;
    private const int CodeLength = PayloadLength + 2;

    private static readonly UInt128 EncodingMaxValue = CalculatePower(BaseVal, DataLength);
    private static readonly Dictionary<char, byte> CharToValueMap = BuildValueMap();

    public static LobbyInfo Generate()
    {
        var randomValue = GetSecureRandomUInt128();
        var valueInRange = randomValue % EncodingMaxValue;
        var validValue = valueInRange - valueInRange % 7;

        return Encode(validValue);
    }

    public static bool TryParse(string input, [NotNullWhen(true)] out LobbyInfo? roomInfo)
    {
        roomInfo = null;
        input = input.Trim();

        if (string.IsNullOrWhiteSpace(input) ||
            !input.StartsWith(FullCodePrefix, StringComparison.OrdinalIgnoreCase) ||
            input.Length != CodeLength)
        {
            return false;
        }

        Span<byte> values = stackalloc byte[DataLength];
        var valueIndex = 0;
        var payloadSpan = input.AsSpan(FullCodePrefix.Length);

        for (var i = 0; i < payloadSpan.Length; i++)
        {
            var ch = payloadSpan[i];
            if (ch == '-')
            {
                if (i != 4 && i != 9 && i != 14)
                {
                    return false;
                }

                continue;
            }

            if (valueIndex >= DataLength ||
                !CharToValueMap.TryGetValue(char.ToUpperInvariant(ch), out var charValue))
            {
                return false;
            }

            values[valueIndex++] = charValue;
        }

        if (valueIndex != DataLength)
        {
            return false;
        }

        UInt128 value = 0;
        for (var i = DataLength - 1; i >= 0; i--)
        {
            value = value * BaseVal + values[i];
        }

        if (value % 7 != 0)
        {
            return false;
        }

        var networkNamePayload = payloadSpan[..9];
        var networkSecretPayload = payloadSpan[10..];

        roomInfo = new LobbyInfo(
            string.Concat(FullCodePrefix, payloadSpan).ToUpperInvariant(),
            string.Concat(NetworkNamePrefix, networkNamePayload).ToLowerInvariant(),
            networkSecretPayload.ToString().ToLowerInvariant());
        return true;
    }

    private static LobbyInfo Encode(UInt128 value)
    {
        var codePayload = string.Create(PayloadLength, value, (span, val) =>
        {
            Span<char> tempChars = stackalloc char[DataLength];
            for (var i = 0; i < DataLength; i++)
            {
                tempChars[i] = Chars[(int)(val % BaseVal)];
                val /= BaseVal;
            }

            tempChars[..4].CopyTo(span[..4]);
            span[4] = '-';
            tempChars[4..8].CopyTo(span[5..9]);
            span[9] = '-';
            tempChars[8..12].CopyTo(span[10..14]);
            span[14] = '-';
            tempChars[12..16].CopyTo(span[15..]);
        });

        var networkNamePayload = codePayload.AsSpan(0, 9);
        var networkSecretPayload = codePayload.AsSpan(10);

        return new LobbyInfo(
            string.Concat(FullCodePrefix, codePayload),
            string.Concat(NetworkNamePrefix, networkNamePayload).ToLowerInvariant(),
            networkSecretPayload.ToString().ToLowerInvariant());
    }

    private static UInt128 GetSecureRandomUInt128()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        var lower = MemoryMarshal.Read<ulong>(bytes);
        var upper = MemoryMarshal.Read<ulong>(bytes[8..]);

        return new UInt128(lower, upper);
    }

    private static UInt128 CalculatePower(uint baseVal, int exp)
    {
        UInt128 result = 1;
        for (var i = 0; i < exp; i++)
        {
            result *= baseVal;
        }

        return result;
    }

    private static Dictionary<char, byte> BuildValueMap()
    {
        var map = new Dictionary<char, byte>(36);
        for (byte i = 0; i < Chars.Length; i++)
        {
            map[Chars[i]] = i;
        }

        map['I'] = 1;
        map['O'] = 0;
        return map;
    }
}
