namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipAuthorityBoundedStream(int maximumBytes) : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count)
    {
        Check(count);
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Check(buffer.Length);
        base.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        Check(1);
        base.WriteByte(value);
    }

    private void Check(int count)
    {
        if (count < 0 || Length > maximumBytes - count)
        { throw new InvalidOperationException("The membership authority payload is too large."); }
    }
}
