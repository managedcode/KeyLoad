using System.Security.Cryptography;

namespace KeyLoad.Server;

/// <summary>Owns a bounded private wire buffer through native protocol replay and response draining.</summary>
internal sealed class McpFrameBody : IDisposable
{
    private const int ScratchCapacityBytes = 16_384;
    private readonly MemoryStream buffer;
    private readonly McpFrameShape shape;
    private MemoryStream? reader;
    private bool disposed;

    private McpFrameBody(MemoryStream buffer, McpFrameShape shape)
    {
        this.buffer = buffer;
        this.shape = shape;
    }

    /// <summary>Gets the exact full wire byte count, including whitespace and protocol metadata.</summary>
    internal int WireBytes
    {
        get { ThrowIfDisposed(); return checked((int)buffer.Length); }
    }

    /// <summary>Gets the full private wire buffer capacity retained by this owner.</summary>
    internal int RetainedCapacity
    {
        get { ThrowIfDisposed(); return buffer.Capacity; }
    }

    /// <summary>Gets the checked structural counts before native SDK parsing allocates its DOM.</summary>
    internal McpFrameShape Shape
    {
        get { ThrowIfDisposed(); return shape; }
    }

    /// <summary>Gets borrowed checked bytes that must not survive this owner's disposal.</summary>
    internal ReadOnlyMemory<byte> Bytes
    {
        get { ThrowIfDisposed(); return buffer.GetBuffer().AsMemory(0, WireBytes); }
    }

    /// <summary>Reads and checks one complete bounded wire frame without retaining an overrun byte.</summary>
    /// <param name="source">The actual request body stream; ownership stays with the caller.</param>
    /// <param name="declaredLength">The optional transport declaration, checked against actual bytes.</param>
    /// <param name="maximumBytes">The inclusive positive wire-byte ceiling.</param>
    /// <param name="cancellationToken">Cancels the read without converting cancellation to a protocol failure.</param>
    /// <returns>A private owner that clears its bytes after the native request and response drain.</returns>
    internal static async Task<McpFrameBody> ReadAsync(Stream source, long? declaredLength,
        int maximumBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (declaredLength is < 0 || declaredLength > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
        cancellationToken.ThrowIfCancellationRequested();
        var initialCapacity = declaredLength.HasValue
            ? checked((int)declaredLength.Value)
            : Math.Min(maximumBytes, ScratchCapacityBytes);
        var retained = new MemoryStream(initialCapacity);
        var transferred = false;
        try
        {
            await ReadBoundedAsync(source, retained, checked((int)(declaredLength ?? maximumBytes)),
                maximumBytes, !declaredLength.HasValue, cancellationToken).ConfigureAwait(false);
            if (declaredLength.HasValue && declaredLength.Value != retained.Length)
            { throw Errors.Fail(ErrorCode.Validation, McpFramingProtocol.InvalidFrame); }
            var checkedShape = McpFrameBounds.Inspect(retained.GetBuffer().AsSpan(0, checked((int)retained.Length)), maximumBytes);
            var body = new McpFrameBody(retained, checkedShape);
            transferred = true;
            return body;
        }
        finally
        {
            if (!transferred)
            { ClearAndClose(retained); }
        }
    }

    /// <summary>Rewinds native replay using a read-only view and closes any preceding view.</summary>
    /// <returns>The current owner-managed reader; closing it does not close the private buffer.</returns>
    internal Stream OpenReader()
    {
        ThrowIfDisposed();
        reader?.Dispose();
        reader = new MemoryStream(buffer.GetBuffer(), 0, WireBytes, writable: false, publiclyVisible: false);
        return reader;
    }

    /// <summary>Closes the replay view and clears the entire private buffer capacity exactly once.</summary>
    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        reader?.Dispose();
        ClearAndClose(buffer);
    }

    private static async Task ReadBoundedAsync(Stream source, MemoryStream retained,
        int wireLimitBytes, int maximumBytes, bool mayGrow, CancellationToken cancellationToken)
    {
        var scratch = new byte[ScratchCapacityBytes];
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = wireLimitBytes - retained.Length;
                var count = checked((int)Math.Min(scratch.Length, remaining + 1));
                var read = await source.ReadAsync(scratch.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                { return; }
                AppendWithinDeclaration(retained, scratch.AsSpan(0, read), remaining,
                    maximumBytes, mayGrow);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scratch);
        }
    }

    private static void AppendWithinDeclaration(MemoryStream retained, ReadOnlySpan<byte> value,
        long remaining, int maximumBytes, bool mayGrow)
    {
        if (value.Length > remaining)
        {
            var exhausted = retained.Length + value.Length > maximumBytes;
            throw Errors.Fail(exhausted ? ErrorCode.ResourceExhausted : ErrorCode.Validation,
                exhausted ? McpFramingProtocol.FrameBudgetExceeded : McpFramingProtocol.InvalidFrame);
        }
        var requiredCapacity = checked((int)(retained.Length + value.Length));
        if (retained.Capacity < requiredCapacity)
        {
            if (!mayGrow)
            { throw new InvalidOperationException(McpCatalogProtocol.InvalidOperation); }
            var doubledCapacity = (long)retained.Capacity * 2;
            var precedingBuffer = retained.GetBuffer();
            retained.Capacity = checked((int)Math.Min(maximumBytes,
                Math.Max(requiredCapacity, doubledCapacity)));
            CryptographicOperations.ZeroMemory(precedingBuffer);
        }
        retained.Write(value);
    }

    private static void ClearAndClose(MemoryStream retained)
    {
        CryptographicOperations.ZeroMemory(retained.GetBuffer());
        retained.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
