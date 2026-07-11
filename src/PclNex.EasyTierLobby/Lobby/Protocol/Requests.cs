using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PclNex.EasyTierLobby.Lobby.Protocol;

internal static class LobbyJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}

internal sealed class PlayerPingRequest(string name, string machineId, string vendor) : IScaffoldingRequest<bool>
{
    private readonly PlayerProfile _profile = new()
    {
        Name = name,
        MachineId = machineId,
        Vendor = vendor
    };

    public string RequestType => "c:player_ping";

    public void WriteRequestBody(IBufferWriter<byte> writer)
    {
        using var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, _profile, LobbyJson.Options);
    }

    public bool ParseResponseBody(ReadOnlyMemory<byte> responseBody) => responseBody.IsEmpty;
}

internal sealed class GetPlayerProfileListRequest : IScaffoldingRequest<IReadOnlyList<PlayerProfile>>
{
    public string RequestType => "c:player_profiles_list";

    public void WriteRequestBody(IBufferWriter<byte> writer)
    {
    }

    public IReadOnlyList<PlayerProfile> ParseResponseBody(ReadOnlyMemory<byte> responseBody)
        => JsonSerializer.Deserialize<IReadOnlyList<PlayerProfile>>(responseBody.Span, LobbyJson.Options) ?? [];
}

internal sealed class GetServerPortRequest : IScaffoldingRequest<ushort>
{
    public string RequestType => "c:server_port";

    public void WriteRequestBody(IBufferWriter<byte> writer)
    {
    }

    public ushort ParseResponseBody(ReadOnlyMemory<byte> responseBody)
    {
        if (responseBody.Length != 2)
        {
            throw new InvalidOperationException("Invalid response body for server port.");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(responseBody.Span);
    }
}

internal sealed class PingRequest(ReadOnlyMemory<byte> payload) : IScaffoldingRequest<ReadOnlyMemory<byte>>
{
    public string RequestType => "c:ping";

    public void WriteRequestBody(IBufferWriter<byte> writer)
    {
        writer.Write(payload.Span);
    }

    public ReadOnlyMemory<byte> ParseResponseBody(ReadOnlyMemory<byte> responseBody) => responseBody;
}

internal sealed class GetProtocolsRequest(IEnumerable<string> supportedProtocols) : IScaffoldingRequest<IReadOnlyList<string>>
{
    public string RequestType => "c:protocols";

    public void WriteRequestBody(IBufferWriter<byte> writer)
    {
        var protocolText = string.Join('\0', supportedProtocols);
        writer.Write(Encoding.ASCII.GetBytes(protocolText));
    }

    public IReadOnlyList<string> ParseResponseBody(ReadOnlyMemory<byte> responseBody)
    {
        if (responseBody.IsEmpty)
        {
            return [];
        }

        return Encoding.ASCII.GetString(responseBody.Span).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
