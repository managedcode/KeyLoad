namespace KeyLoad.Core.Features.ResourceExecution;

internal sealed class ResultByteCounterStream(long maximumBytes, Action check) : Stream
{
    private const string ResultBytesExceeded = "The read result byte budget is exceeded.";
    private long bytes;

    internal static long Measure<T>(ReadExecutionBudget budget, T result)
    {
        budget.Check();
        using var counter = new ResultByteCounterStream(budget.MaximumResultBytes, budget.Check);
        System.Text.Json.JsonSerializer.Serialize(counter, result, JsonDefaults.Options);
        budget.Check();
        return counter.Length;
    }

    internal static long MeasureNative<T>(ReadExecutionBudget budget, T result)
    {
        budget.Check();
        using var counter = new ResultByteCounterStream(budget.MaximumResultBytes, budget.Check);
        NativeSerialization.Serialize(result, counter);
        budget.Check();
        return counter.Length;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => bytes;
    public override long Position
    {
        get => bytes;
        set => throw new NotSupportedException();
    }

    public override void Flush() => check();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        check();
        if (buffer.Length > maximumBytes - bytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResultBytesExceeded);
        }
        bytes += buffer.Length;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
