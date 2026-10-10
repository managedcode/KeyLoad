namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Retains only the original selected endpoint bytes under its unchanged reply bound.</summary>
internal sealed class RemoteTransferOriginalResponseBuffer(int maximumBytes) : MemoryStream
{
    private void Require(int count)
    {
        if (count < 0 || Position > maximumBytes || count > maximumBytes - Position)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
    }

    public override void Write(byte[] buffer, int offset, int count)
    { Require(count); base.Write(buffer, offset, count); }

    public override void Write(ReadOnlySpan<byte> buffer)
    { Require(buffer.Length); base.Write(buffer); }

    public override void WriteByte(byte value)
    { Require(1); base.WriteByte(value); }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    { Require(count); return base.WriteAsync(buffer, offset, count, cancellationToken); }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    { Require(buffer.Length); return base.WriteAsync(buffer, cancellationToken); }

    public override void SetLength(long value)
    {
        if (value < 0 || value > maximumBytes)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        base.SetLength(value);
    }
}
