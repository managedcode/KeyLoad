using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns a bounded private wire buffer through native protocol replay and response draining.</summary>
internal sealed class McpFrameBody : IDisposable
{
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
        get {
            const int StartEmptyCount = 0;
 ThrowIfDisposed(); return buffer.GetBuffer().AsMemory(StartEmptyCount, WireBytes); }
    }

    /// <summary>Reads and checks one complete bounded wire frame without retaining an overrun byte.</summary>
    /// <param name="source">The actual request body stream; ownership stays with the caller.</param>
    /// <param name="declaredLength">The optional transport declaration, checked against actual bytes.</param>
    /// <param name="maximumBytes">The inclusive positive wire-byte ceiling.</param>
    /// <param name="options">The centrally validated native MCP ingress and framing policy.</param>
    /// <param name="cancellationToken">Cancels the read without converting cancellation to a protocol failure.</param>
    /// <returns>A private owner that clears its bytes after the native request and response drain.</returns>
    internal static async Task<McpFrameBody> ReadAsync(Stream source, long? declaredLength,
        int maximumBytes, IOptions<McpExecutionOptions> options, CancellationToken cancellationToken = default)
    {
        const int DeclaredLengthEmptyCount = 0;
        const int StartEmptyCount = 0;

        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (declaredLength is < DeclaredLengthEmptyCount || declaredLength > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        settings.Validate();
        var initialCapacity = declaredLength.HasValue
            ? checked((int)declaredLength.Value)
            : Math.Min(maximumBytes, settings.IngressScratchBytes);
        var retained = new MemoryStream(initialCapacity);
        var transferred = false;
        try
        {
            await ReadBoundedAsync(source, retained, checked((int)(declaredLength ?? maximumBytes)),
                maximumBytes, !declaredLength.HasValue, settings.IngressScratchBytes, cancellationToken).ConfigureAwait(false);
            if (declaredLength.HasValue && declaredLength.Value != retained.Length)
            { throw Errors.Fail(ErrorCode.Validation, McpFramingProtocol.InvalidFrame); }
            var checkedShape = McpFrameBounds.Inspect(retained.GetBuffer().AsSpan(StartEmptyCount, checked((int)retained.Length)), maximumBytes, options);
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
        const int IndexEmptyCount = 0;

        ThrowIfDisposed();
        reader?.Dispose();
        reader = new MemoryStream(buffer.GetBuffer(), IndexEmptyCount, WireBytes, writable: false, publiclyVisible: false);
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
        int wireLimitBytes, int maximumBytes, bool mayGrow, int scratchCapacityBytes, CancellationToken cancellationToken)
    {
        const int RemainingStep = 1;
        const int StartEmptyCount = 0;
        const int EmptyRead = 0;

        var scratch = new byte[scratchCapacityBytes];
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = wireLimitBytes - retained.Length;
                var count = checked((int)Math.Min(scratch.Length, remaining + RemainingStep));
                var read = await source.ReadAsync(scratch.AsMemory(StartEmptyCount, count), cancellationToken).ConfigureAwait(false);
                if (read == EmptyRead)
                { return; }
                AppendWithinDeclaration(retained, scratch.AsSpan(StartEmptyCount, read), remaining,
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
            const int CapacityDoublingFactor = 2;
            var doubledCapacity = (long)retained.Capacity * CapacityDoublingFactor;
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
