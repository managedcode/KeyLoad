using System.Text;
using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-004: the actual owned stream rejects overflow before appending and clears its private capacity.</summary>
internal sealed class McpBoundedWriteStreamTests
{
    private delegate int SpanRead(Span<byte> destination);
    private delegate int ArrayRead(byte[] buffer, int offset, int count);

    private const int MaximumBytes = 64;
    private const string PrivateMarker = "private-bounded-marker";

    /// <summary>Native JSON writes copy only accepted bytes; the returned owner survives private-buffer clearing.</summary>
    [Test]
    public async Task AcMcp004NativeSerializerReturnsAnIndependentExactLengthOwner()
    {
        var expected = JsonDefaults.Serialize(PrivateMarker);
        using var stream = new McpBoundedWriteStream(MaximumBytes);
        var borrowed = stream.BorrowBuffer();
        JsonSerializer.Serialize(stream, PrivateMarker, JsonDefaults.Options);
        var owned = stream.ToOwnedArray();

        await Assert.That(borrowed.Length).IsEqualTo(MaximumBytes);
        await Assert.That(owned.Length).IsEqualTo(expected.Length);
        await Assert.That(owned.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(borrowed.Span[..expected.Length].SequenceEqual(expected)).IsTrue();
        DisposeTwice(stream);
        await Assert.That(borrowed.Span.SequenceEqual(new byte[MaximumBytes])).IsTrue();
        await Assert.That(owned.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    /// <summary>A failed native write cannot append any bytes and disposal clears the entire prior private buffer.</summary>
    [Test]
    public async Task AcMcp004OverrunPreservesAcceptedPrefixThenClearsFullCapacity()
    {
        var prefix = Encoding.UTF8.GetBytes(PrivateMarker);
        using var stream = new McpBoundedWriteStream(prefix.Length);
        var borrowed = stream.BorrowBuffer();
        stream.Write(prefix.AsSpan());
        var error = Assert.ThrowsExactly<KeyLoadException>(() => JsonSerializer.Serialize(stream, PrivateMarker, JsonDefaults.Options));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(error.Message).DoesNotContain(PrivateMarker);
        await Assert.That(stream.ToOwnedArray().AsSpan().SequenceEqual(prefix)).IsTrue();
        await Assert.That(borrowed.Length).IsEqualTo(prefix.Length);
        DisposeTwice(stream);
        await Assert.That(borrowed.Span.SequenceEqual(new byte[borrowed.Length])).IsTrue();
    }

    /// <summary>Canonical converter exceptions also leave clearing to the same deterministic stream owner.</summary>
    [Test]
    public async Task AcMcp004NativeConverterFailureDoesNotMaskItsErrorDuringCleanup()
    {
        var prefix = Encoding.UTF8.GetBytes(PrivateMarker);
        using var stream = new McpBoundedWriteStream(MaximumBytes);
        var borrowed = stream.BorrowBuffer();
        stream.Write(prefix.AsSpan());
        var invalid = new CommandRequest(McpCanonicalTestData.StableId, McpCanonicalTestData.Partition, default);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(stream, invalid, JsonDefaults.Options));
        DisposeTwice(stream);
        await Assert.That(borrowed.Span.SequenceEqual(new byte[MaximumBytes])).IsTrue();
    }

    /// <summary>Span, legacy array, byte and empty writes share the same inclusive byte ceiling.</summary>
    [Test]
    public async Task AcMcp004EverySynchronousWritePathSharesTheFixedCapacity()
    {
        var bytes = Encoding.UTF8.GetBytes(PrivateMarker);
        var (written, afterOverflow) = ExerciseSynchronousWritePaths(bytes);
        await Assert.That(written.AsSpan().SequenceEqual(bytes)).IsTrue();
        await Assert.That(afterOverflow.AsSpan().SequenceEqual(bytes)).IsTrue();
    }

    /// <summary>Reads, seeking and length mutation remain unsupported on the real write-only owner.</summary>
    [Test]
    public async Task AcMcp004WriteOnlyCapabilitiesRejectUnsupportedOperations()
    {
        var supports = CheckSynchronousReadAndSeekPaths();
        await Assert.That(supports.CanRead).IsFalse();
        await Assert.That(supports.CanSeek).IsFalse();
        await Assert.That(supports.CanWrite).IsTrue();
    }

    /// <summary>Disposal disables writing and every retained-data access without leaving readable private bytes.</summary>
    [Test]
    public async Task AcMcp004DisposedOwnerGuardsWritesCopiesAndBorrowing()
    {
        var canWrite = CheckSynchronousDisposedOwnerPaths();
        await Assert.That(canWrite).IsFalse();
    }

    /// <summary>Invalid legacy array ranges preserve standard argument validation and leave the owner empty.</summary>
    [Test]
    public async Task AcMcp004InvalidArrayRangeNeverWritesPrivateBytes()
    {
        var retainedBytes = CheckInvalidSynchronousArrayRanges();
        await Assert.That(retainedBytes).IsEqualTo(0);
    }

    private static void DisposeTwice(McpBoundedWriteStream stream)
    {
        stream.Dispose();
        stream.Dispose();
    }

    private static (byte[] Accepted, byte[] AfterOverflow) ExerciseSynchronousWritePaths(byte[] bytes)
    {
        using var stream = new McpBoundedWriteStream(bytes.Length);
        stream.Write(bytes, 0, bytes.Length - 1);
        stream.WriteByte(bytes[^1]);
        stream.Write(ReadOnlySpan<byte>.Empty);
        stream.Flush();
        var exact = stream.ToOwnedArray();
        Assert.ThrowsExactly<KeyLoadException>(() => stream.WriteByte(byte.MaxValue));
        Assert.ThrowsExactly<KeyLoadException>(() => stream.Write(bytes.AsSpan()));
        return (exact, stream.ToOwnedArray());
    }

    private static (bool CanRead, bool CanSeek, bool CanWrite) CheckSynchronousReadAndSeekPaths()
    {
        using var stream = new McpBoundedWriteStream(MaximumBytes);
        ArrayRead arrayRead = stream.Read;
        Assert.ThrowsExactly<NotSupportedException>(() => arrayRead(new byte[1], 0, 1));
        SpanRead spanRead = stream.Read;
        Assert.ThrowsExactly<NotSupportedException>(() => spanRead(new byte[1]));
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.ThrowsExactly<NotSupportedException>(() => stream.SetLength(MaximumBytes));
        Assert.ThrowsExactly<NotSupportedException>(() => _ = stream.Length);
        Assert.ThrowsExactly<NotSupportedException>(() => _ = stream.Position);
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Position = 0);
        return (stream.CanRead, stream.CanSeek, stream.CanWrite);
    }

    private static bool CheckSynchronousDisposedOwnerPaths()
    {
        var stream = new McpBoundedWriteStream(MaximumBytes);
        stream.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => stream.Write(new byte[1].AsSpan()));
        Assert.ThrowsExactly<ObjectDisposedException>(() => stream.WriteByte(byte.MaxValue));
        Assert.ThrowsExactly<ObjectDisposedException>(stream.Flush);
        Assert.ThrowsExactly<ObjectDisposedException>(() => stream.ToOwnedArray());
        Assert.ThrowsExactly<ObjectDisposedException>(() => stream.BorrowBuffer());
        return stream.CanWrite;
    }

    private static int CheckInvalidSynchronousArrayRanges()
    {
        using var stream = new McpBoundedWriteStream(MaximumBytes);
        Assert.ThrowsExactly<ArgumentNullException>(() => stream.Write(null!, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => stream.Write(new byte[1], -1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => stream.Write(new byte[1], 0, 2));
        return stream.ToOwnedArray().Length;
    }
}
