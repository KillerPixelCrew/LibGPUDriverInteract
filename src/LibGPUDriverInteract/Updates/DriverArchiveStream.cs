namespace LibGPUDriverInteract;

/// <summary>A non-owning, seekable view of the archive in a held self-extracting package.</summary>
internal sealed class DriverArchiveStream(Stream input, long offset) : Stream
{
    private long _position;
    public override bool CanRead => input.CanRead;
    public override bool CanSeek => input.CanSeek;
    public override bool CanWrite => false;
    public override long Length => input.Length - offset;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int bufferOffset, int count) => Read(buffer.AsSpan(bufferOffset, count));

    public override int Read(Span<byte> buffer)
    {
        input.Position = offset + _position;
        var count = input.Read(buffer[..(int)Math.Min(buffer.Length, Length - _position)]);
        _position += count;
        return count;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        input.Position = offset + _position;
        var count = await input.ReadAsync(buffer[..(int)Math.Min(buffer.Length, Length - _position)], token)
            .ConfigureAwait(false);
        _position += count;
        return count;
    }

    public override long Seek(long value, SeekOrigin origin)
    {
        var position = origin switch
        {
            SeekOrigin.Begin => value,
            SeekOrigin.Current => checked(_position + value),
            SeekOrigin.End => checked(Length + value),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (position < 0 || position > Length)
        {
            throw new IOException("Archive seek escaped the held package.");
        }

        return _position = position;
    }

    public override void Flush()
    {
    }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int bufferOffset, int count) => throw new NotSupportedException();
}
