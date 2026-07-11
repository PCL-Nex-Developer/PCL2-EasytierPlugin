using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;

namespace PclNex.EasyTierLobby.Lobby.Protocol;

internal static class ProtocolWriter
{
    public static async ValueTask WriteRequestAsync<T>(
        PipeWriter writer,
        IScaffoldingRequest<T> request,
        CancellationToken cancellationToken = default)
    {
        var bodyWriter = new ArrayBufferWriter<byte>();
        request.WriteRequestBody(bodyWriter);
        var requestBody = bodyWriter.WrittenMemory;

        var requestTypeBytes = Encoding.ASCII.GetBytes(request.RequestType);
        if (requestTypeBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Request type is too long.");
        }

        writer.GetSpan(1)[0] = (byte)requestTypeBytes.Length;
        writer.Advance(1);
        writer.Write(requestTypeBytes);

        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)requestBody.Length);
        writer.Write(lengthBytes);

        if (!requestBody.IsEmpty)
        {
            writer.Write(requestBody.Span);
        }

        var result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsCanceled)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
