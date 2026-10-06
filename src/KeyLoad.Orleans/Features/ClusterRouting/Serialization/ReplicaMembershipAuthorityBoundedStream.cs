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
        const int CountSingleItemCount = 1;

        Check(CountSingleItemCount);
        base.WriteByte(value);
    }

    private void Check(int count)
    {
        const int CountValidationBoundary = 0;
        const string CheckFailureMessage = "The membership authority payload is too large.";

        if (count < CountValidationBoundary || Length > maximumBytes - count)
        { throw new InvalidOperationException(CheckFailureMessage); }
    }
}
