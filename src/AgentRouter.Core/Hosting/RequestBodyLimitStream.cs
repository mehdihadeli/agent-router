namespace AgentRouter.Core.Hosting;

internal sealed class RequestBodyLimitStream(Stream inner, long limit) : Stream
{
    private long bytesRead;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Record(inner.Read(buffer, offset, ReadSize(count)));

    public override int Read(Span<byte> buffer) =>
        Record(inner.Read(buffer[..ReadSize(buffer.Length)]));

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => Record(await inner.ReadAsync(buffer, offset, ReadSize(count), cancellationToken));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => Record(await inner.ReadAsync(buffer[..ReadSize(buffer.Length)], cancellationToken));

    private int ReadSize(int count) => (int)Math.Min(count, limit - bytesRead + 1);

    private int Record(int count)
    {
        bytesRead += count;
        if (bytesRead > limit)
            throw new BadHttpRequestException(
                "Request body is too large.",
                StatusCodes.Status413PayloadTooLarge
            );
        return count;
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
