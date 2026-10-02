using System.Text;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-004/005: actual stream asynchronous entry points preserve cancellation and owned-byte bounds.</summary>
internal sealed class McpBoundedWriteStreamAsyncTests
{
    private const int MaximumBytes = 64;
    private const string PrivateMarker = "private-async-marker";

    /// <summary>Async memory and array writes use the same private byte owner and do not append after overflow.</summary>
    [Test]
    public async Task AcMcp004AsyncMemoryAndArrayWritesKeepTheSameInclusiveCeiling()
    {
        var bytes = Encoding.UTF8.GetBytes(PrivateMarker);
        using var stream = new McpBoundedWriteStream(bytes.Length);
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length - 1));
        Func<byte[], int, int, Task> arrayWriteAsync = stream.WriteAsync;
        await arrayWriteAsync(bytes, bytes.Length - 1, 1);
        await stream.FlushAsync();

        await Assert.That(stream.ToOwnedArray().AsSpan().SequenceEqual(bytes)).IsTrue();
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => stream.WriteAsync(bytes.AsMemory()).AsTask())
            ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(stream.ToOwnedArray().AsSpan().SequenceEqual(bytes)).IsTrue();
    }

    /// <summary>Already-cancelled writes and flush preserve their exact cancellation token without changing private bytes.</summary>
    [Test]
    public async Task AcMcp005CancellationRetainsItsOriginalTokenAndNeverWrites()
    {
        var bytes = Encoding.UTF8.GetBytes(PrivateMarker);
        using var stream = new McpBoundedWriteStream(MaximumBytes);
        var borrowed = stream.BorrowBuffer();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var memoryError = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            stream.WriteAsync(bytes.AsMemory(), cancellation.Token).AsTask()) ?? throw new InvalidOperationException();
        var arrayError = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            stream.WriteAsync(bytes, 0, bytes.Length, cancellation.Token)) ?? throw new InvalidOperationException();
        var flushError = await Assert.ThrowsAsync<OperationCanceledException>(() => stream.FlushAsync(cancellation.Token))
            ?? throw new InvalidOperationException();
        await Assert.That(memoryError.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(arrayError.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(flushError.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(stream.ToOwnedArray().Length).IsEqualTo(0);
        await Assert.That(borrowed.Span.SequenceEqual(new byte[MaximumBytes])).IsTrue();
    }

    /// <summary>Async reads remain unsupported without introducing a hidden read buffer; cancellation keeps its token.</summary>
    [Test]
    public async Task AcMcp004AsyncReadsRemainUnsupportedOnTheRealWriteOnlyOwner()
    {
        using var stream = new McpBoundedWriteStream(MaximumBytes);
        var destination = new byte[1];
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => stream.ReadAsync(destination.AsMemory()).AsTask());
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => stream.ReadAsync(destination, 0, destination.Length));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            stream.ReadAsync(destination.AsMemory(), cancellation.Token).AsTask()) ?? throw new InvalidOperationException();
        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(stream.ToOwnedArray().Length).IsEqualTo(0);
    }

    /// <summary>Framework async disposal clears the same native buffer and guards later asynchronous writes.</summary>
    [Test]
    public async Task AcMcp004AsyncDisposalClearsFullPrivateCapacity()
    {
        var bytes = Encoding.UTF8.GetBytes(PrivateMarker);
        await using var stream = new McpBoundedWriteStream(MaximumBytes);
        var borrowed = stream.BorrowBuffer();
        await stream.WriteAsync(bytes.AsMemory());
        await stream.DisposeAsync();

        await Assert.That(borrowed.Span.SequenceEqual(new byte[MaximumBytes])).IsTrue();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => stream.WriteAsync(bytes.AsMemory()).AsTask());
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(stream.FlushAsync);
    }
}
