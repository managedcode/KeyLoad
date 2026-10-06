using Microsoft.Extensions.Options;
using System.Buffers;

namespace KeyLoad.Orleans;

internal sealed class GrainNativeCountingWriter : IBufferWriter<byte>, IDisposable
{
    private const int UnspecifiedSizeHint = 0;
    private readonly GrainRoutingOptions settings;
    private readonly int maximumBytes;
    private readonly CancellationToken cancellationToken;
    private byte[] scratch = Array.Empty<byte>();
    private bool disposed;

    internal GrainNativeCountingWriter(int maximumBytes, IOptions<GrainRoutingOptions> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        settings = options.Value;
        settings.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        this.maximumBytes = maximumBytes;
        this.cancellationToken = cancellationToken;
    }

    internal long Length { get; private set; }

    public void Advance(int count)
    {
        const int StartEmptyCount = 0;

        CheckAvailable();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, scratch.Length);
        if (count > maximumBytes - Length)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }

        Length = checked(Length + count);
        scratch.AsSpan(StartEmptyCount, count).Clear();
    }

    public Memory<byte> GetMemory(int sizeHint = UnspecifiedSizeHint)
    {
        EnsureCapacity(sizeHint);
        return scratch;
    }

    public Span<byte> GetSpan(int sizeHint = UnspecifiedSizeHint)
    {
        EnsureCapacity(sizeHint);
        return scratch;
    }

    private void EnsureCapacity(int sizeHint)
    {
        CheckAvailable();
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        var required = Math.Max(sizeHint, settings.MinimumScratchBytes);
        if (required > settings.MaximumScratchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }

        if (scratch.Length >= required)
        {
            return;
        }

        scratch.AsSpan().Clear();
        scratch = new byte[required];
    }

    private void CheckAvailable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        scratch.AsSpan().Clear();
        scratch = Array.Empty<byte>();
        disposed = true;
    }
}
