using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises the exact real wire reader used by the original-outcome endpoint.</summary>
internal sealed class PartitionMovementOutcomeWireTests
{
    private const int MaximumBytes = 4_096;
    private static readonly byte[] OriginalBytes = [1, 2, 3, 4];

    [Test]
    public async Task OutcomePathReadsExactOriginalBytesAndWrongPurposeRejectsBeforeHealthyNextRequest()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var first = Request(PartitionMovementProtocol.OutcomePath);
        var body = await PartitionMovementWire.ReadOutcomeAsync(first.Context.Request, MaximumBytes, token);
        await Assert.That(body.SequenceEqual(OriginalBytes)).IsTrue();
        using var wrong = Request(PartitionMovementProtocol.Path);
        var rejected = await Assert.ThrowsAsync<KeyLoadException>(() =>
            PartitionMovementWire.ReadOutcomeAsync(wrong.Context.Request, MaximumBytes, token));
        await Assert.That(rejected!.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(wrong.Body.Position).IsEqualTo(0L);
        using var phaseWrong = Request(PartitionMovementProtocol.OutcomePath);
        var phaseRejected = await Assert.ThrowsAsync<KeyLoadException>(() =>
            PartitionMovementWire.ReadAsync(phaseWrong.Context.Request, MaximumBytes, token));
        await Assert.That(phaseRejected!.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(phaseWrong.Body.Position).IsEqualTo(0L);
        using var healthy = Request(PartitionMovementProtocol.OutcomePath);
        var next = await PartitionMovementWire.ReadOutcomeAsync(healthy.Context.Request, MaximumBytes, token);
        await Assert.That(next.SequenceEqual(OriginalBytes)).IsTrue();
        using var phase = Request(PartitionMovementProtocol.Path);
        var phaseBody = await PartitionMovementWire.ReadAsync(phase.Context.Request, MaximumBytes, token);
        await Assert.That(phaseBody.SequenceEqual(OriginalBytes)).IsTrue();
    }

    private static WireRequest Request(string path) => new(path);

    private sealed class WireRequest : IDisposable
    {
        internal WireRequest(string path)
        {
            Body = new MemoryStream(OriginalBytes, writable: false);
            Context = new DefaultHttpContext();
            Context.Request.Method = HttpMethods.Post;
            Context.Request.Path = path;
            Context.Request.ContentType = PartitionMovementProtocol.ContentType;
            Context.Request.ContentLength = OriginalBytes.Length;
            Context.Request.Body = Body;
        }

        internal DefaultHttpContext Context { get; }
        internal MemoryStream Body { get; }
        public void Dispose() => Body.Dispose();
    }
}
