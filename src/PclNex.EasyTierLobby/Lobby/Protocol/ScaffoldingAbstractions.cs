using System.Buffers;

namespace PclNex.EasyTierLobby.Lobby.Protocol;

internal interface IScaffoldingRequest<out TResponse>
{
    string RequestType { get; }

    void WriteRequestBody(IBufferWriter<byte> writer);

    TResponse ParseResponseBody(ReadOnlyMemory<byte> responseBody);
}

internal sealed record ScaffoldingResponse(byte Status, ReadOnlyMemory<byte> Body);

internal sealed class ScaffoldingRequestException(byte status, string? serverMessage = null)
    : Exception(serverMessage ?? $"Scaffolding request failed with status {status}.")
{
    public byte Status { get; } = status;
}
