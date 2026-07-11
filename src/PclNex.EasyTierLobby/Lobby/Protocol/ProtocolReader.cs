using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;

namespace PclNex.EasyTierLobby.Lobby.Protocol;

internal static class ProtocolReader
{
    public static async ValueTask<ScaffoldingResponse> ReadResponseAsync(
        PipeReader reader,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (TryParseResponse(ref buffer, out var response))
            {
                reader.AdvanceTo(buffer.Start);
                return response;
            }

            reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);

            if (result.IsCompleted)
            {
                throw new InvalidOperationException("Connection closed unexpectedly.");
            }
        }
    }

    private static bool TryParseResponse(ref ReadOnlySequence<byte> buffer, out ScaffoldingResponse response)
    {
        response = null!;
        if (buffer.Length < 5)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[5];
        buffer.Slice(0, 5).CopyTo(header);

        var status = header[0];
        var bodyLength = BinaryPrimitives.ReadUInt32BigEndian(header[1..]);
        var fullPacketLength = 5 + bodyLength;
        if (buffer.Length < fullPacketLength)
        {
            return false;
        }

        var body = buffer.Slice(5, bodyLength).ToArray();
        buffer = buffer.Slice(fullPacketLength);
        response = new ScaffoldingResponse(status, body);

        if (response.Status != 0)
        {
            var serverMessage = response.Status == 255 ? Encoding.UTF8.GetString(response.Body.Span) : null;
            throw new ScaffoldingRequestException(response.Status, serverMessage);
        }

        return true;
    }
}
