namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// A write-only <see cref="Stream"/> that checks what is written against an expected buffer instead of storing it.
/// Serializing a catalog purely to answer "would this differ?" then costs no second copy of the output: the format
/// writes through, the bytes are compared as they arrive, and nothing is retained.
/// </summary>
internal sealed class ComparisonStream(ReadOnlyMemory<byte> expected) : Stream
{
    private readonly ReadOnlyMemory<byte> _expected = expected;
    private int _written;
    private bool _diverged;

    /// <summary>Whether everything written matched the expected buffer exactly, and covered all of it.</summary>
    public bool Matched => !_diverged && _written == _expected.Length;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _expected.Length;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        // Once the output has diverged the answer cannot change, so the rest is accepted and dropped rather than
        // compared — the format has no way to be told to stop mid-write.
        if (_diverged)
        {
            return;
        }

        if (buffer.Length > _expected.Length - _written
            || !buffer.SequenceEqual(_expected.Span.Slice(_written, buffer.Length)))
        {
            _diverged = true;
            return;
        }

        _written += buffer.Length;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
