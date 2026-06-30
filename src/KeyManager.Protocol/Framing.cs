using System.Buffers.Binary;
using System.Text.Json;

namespace KeyManager.Protocol;

/// <summary>
/// 길이 프리픽스(4바이트 big-endian) + UTF-8 JSON 한 메시지를 읽고 쓴다.
/// 텍스트 구분자(':' / ']') 깨짐 문제를 피하기 위해 JSON 직렬화 사용(설계 §9).
/// </summary>
public static class Framing
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // 와이어는 컴팩트하게. 기본 PascalCase 프로퍼티명 사용(양쪽 동일 타입이라 무방).
        WriteIndented = false,
    };

    public static async Task WriteMessageAsync<T>(Stream stream, T message, CancellationToken ct = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOpts);
        if (body.Length > ProtocolConstants.MaxFrameBytes)
            throw new InvalidOperationException($"메시지가 너무 큽니다: {body.Length} bytes");

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<T> ReadMessageAsync<T>(Stream stream, CancellationToken ct = default)
    {
        byte[] header = await ReadExactlyAsync(stream, 4, ct).ConfigureAwait(false);
        int len = BinaryPrimitives.ReadInt32BigEndian(header);
        if (len < 0 || len > ProtocolConstants.MaxFrameBytes)
            throw new InvalidOperationException($"잘못된 프레임 길이: {len}");

        byte[] body = await ReadExactlyAsync(stream, len, ct).ConfigureAwait(false);
        T? msg = JsonSerializer.Deserialize<T>(body, JsonOpts);
        return msg ?? throw new InvalidOperationException($"메시지 역직렬화 실패: {typeof(T).Name}");
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken ct)
    {
        byte[] buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n == 0)
                throw new EndOfStreamException("상대가 연결을 닫았습니다.");
            read += n;
        }
        return buffer;
    }
}
