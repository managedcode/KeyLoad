using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Caps a previously unissued phase by the original wholeflow deadline and central signed lifetime.</summary>
internal static class ControlledPartitionMovementFirstPhaseExpiry
{
    internal static DateTimeOffset Create(ControlledPartitionMovementNode owner, ServerRuntimeOptions runtime,
        DateTimeOffset wholeExpiresAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = owner.Database.EvaluationClock.GetUtcNow();
        if (wholeExpiresAt <= now)
        { throw new TimeoutException("The original movement wholeflow deadline elapsed."); }
        var first = now + runtime.GrainRouting.Value.RequestLifetime;
        return first < wholeExpiresAt ? first : wholeExpiresAt;
    }
}
