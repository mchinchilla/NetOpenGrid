namespace NetOpenGrid.Infrastructure.Export;

/// <summary>
/// Lets <see cref="System.IO.Compression.ZipArchive"/> write to an async-only stream (Kestrel's
/// response body). Even its async API still writes a few headers and the central directory
/// synchronously; those small writes are held here and sent with the next async write or flush.
/// The bulk of the data (the compressed entries) goes straight through.
/// </summary>
internal sealed class SyncWriteBufferStream(Stream inner) : Stream
{
    private readonly MemoryStream _pending = new();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => _pending.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => _pending.Write(buffer);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await DrainAsync(cancellationToken);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await DrainAsync(cancellationToken);
        await inner.FlushAsync(cancellationToken);
    }

    /// <summary>Nothing is written synchronously to the inner stream: pending bytes wait for <see cref="FlushAsync"/>.</summary>
    public override void Flush()
    {
    }

    private async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        if (_pending.Length == 0)
        {
            return;
        }

        await inner.WriteAsync(_pending.GetBuffer().AsMemory(0, (int)_pending.Length), cancellationToken);
        _pending.SetLength(0);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
