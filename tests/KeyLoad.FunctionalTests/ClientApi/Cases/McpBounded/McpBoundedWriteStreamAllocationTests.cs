using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>AC-MCP-004: a payload ceiling bounds accepted bytes without allocating that ceiling for every request.</summary>
internal sealed class McpBoundedWriteStreamAllocationTests
{
    private const int FirstWriteBytes = 3072;
    private const int SecondWriteBytes = 6144;
    private const byte PrivateByte = 173;

    [Test]
    public async Task GrowingWritesClearRetiredCapacityAndPreserveAcceptedBytesThroughOverflow()
    {
        using var stream = new McpBoundedWriteStream(FirstWriteBytes + SecondWriteBytes);
        var first = Enumerable.Repeat(PrivateByte, FirstWriteBytes).ToArray();
        stream.Write(first);
        var retired = stream.BorrowBuffer();
        var second = Enumerable.Repeat(PrivateByte, SecondWriteBytes).ToArray();
        stream.Write(second);
        var current = stream.BorrowBuffer();
        var expected = first.Concat(second).ToArray();
        var owned = stream.ToOwnedArray();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => stream.WriteByte(PrivateByte));

        await Assert.That(retired.Span.ContainsAnyExcept((byte)0)).IsFalse();
        await Assert.That(owned.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(stream.ToOwnedArray().AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await stream.DisposeAsync();
        await Assert.That(current.Span.ContainsAnyExcept((byte)0)).IsFalse();
        await Assert.That(owned.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task GrowingWriteFromItsOwnBorrowedBytesRetainsTheOriginalContent()
    {
        using var stream = new McpBoundedWriteStream(FirstWriteBytes * 2);
        var content = Enumerable.Repeat(PrivateByte, FirstWriteBytes).ToArray();
        stream.Write(content);
        var retired = stream.BorrowBuffer();
        stream.Write(retired.Span[..content.Length]);

        await Assert.That(stream.ToOwnedArray().AsSpan().SequenceEqual(content.Concat(content).ToArray())).IsTrue();
        await Assert.That(retired.Span.ContainsAnyExcept((byte)0)).IsFalse();
    }
}
