using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Includes the original inner bytes, both MACs and nonce in the complete native outer budget.</summary>
internal static class ControlledPartitionMovementNativeOuterBudget
{
    private const long BoundaryStep = 1;
    private const string WitnessKind = "HostedMovementCompleteSignedOuterReplyBudget";

    internal static async Task AssertAsync(PartitionMovementGrainServiceReply original, int maximumBytes,
        CancellationToken token)
    {
        var count = NativeSerialization.Measure(original);
        await Console.Out.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            Kind = WitnessKind,
            FullOuterBytes = count,
            InnerReplyBytes = original.Payload.Length,
            MaximumBytes = maximumBytes
        }));
        await Assert.That(count).IsLessThanOrEqualTo(maximumBytes);
        using var below = new ResultByteCounterStream(checked(count - BoundaryStep), token.ThrowIfCancellationRequested);
        var refusal = Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(original, below));
        await Assert.That(refusal.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        using var healthy = new ResultByteCounterStream(maximumBytes, token.ThrowIfCancellationRequested);
        NativeSerialization.Serialize(original, healthy);
        await Assert.That(healthy.Length).IsEqualTo(count);
        var bytes = NativeSerialization.Serialize(original);
        var decoded = NativeSerialization.Deserialize<PartitionMovementGrainServiceReply>(bytes);
        await Assert.That(NativeSerialization.Serialize(decoded).AsSpan().SequenceEqual(bytes)).IsTrue();
        await Assert.That(decoded.Payload.Span.SequenceEqual(original.Payload.Span)).IsTrue();
        await Assert.That(decoded.Signature).IsEqualTo(original.Signature);
        await Assert.That(decoded.SenderSignature).IsEqualTo(original.SenderSignature);
        await Assert.That(decoded.Nonce).IsEqualTo(original.Nonce);
        await Assert.That(decoded.Error).IsEqualTo(original.Error);
        await Assert.That(decoded.SafeDetail).IsEqualTo(original.SafeDetail);
    }
}
