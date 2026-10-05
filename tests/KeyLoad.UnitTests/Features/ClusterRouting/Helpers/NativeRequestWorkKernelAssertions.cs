using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeRequestWorkKernelAssertions
{
    private const int ObservationMilliseconds = 100;
    private const int SettlementSeconds = 10;
    private static readonly TimeSpan ObservationBound = TimeSpan.FromMilliseconds(ObservationMilliseconds);
    private static readonly TimeSpan SettlementBound = TimeSpan.FromSeconds(SettlementSeconds);

    internal static async Task AssertHealthyFollowingStreamAsync(RequestCqrsClusterFixture fixture)
    {
        await using var owner = new NativeRequestWorkOwner(UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var probe = new NativeRequestWorkProbe();
        probe.ReleaseProducer();
        var chunks = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        await foreach (var chunk in NativeRequestWorkKernelCases.CreateStream(fixture, owner, requestId, probe))
        {
            chunks.Add(chunk);
        }

        await Assert.That(chunks.Count).IsEqualTo(2);
        await Assert.That(chunks[0].Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(chunks[1].Kind).IsEqualTo(CqrsStreamChunkKind.Completed);
        await Assert.That(probe.ProducerSettleCount).IsEqualTo(1);
        await Assert.That(probe.ActivationSettleCount).IsEqualTo(1);
        await owner.DrainAsync().WaitAsync(SettlementBound);
    }

    internal static async Task AssertStillRunningAsync(Task operation)
    {
        using var window = new CancellationTokenSource();
        var observation = Task.Delay(ObservationBound, window.Token);
        Task completed;
        try
        {
            completed = await Task.WhenAny(operation, observation);
        }
        finally
        {
            await window.CancelAsync();
        }
        await ObserveCancelledDelayAsync(observation, window);
        await Assert.That(!ReferenceEquals(completed, operation)).IsTrue();
    }

    internal static async Task AssertBothRunningAsync(Task first, Task second)
    {
        using var window = new CancellationTokenSource();
        var observation = Task.Delay(ObservationBound, window.Token);
        Task completed;
        try
        {
            completed = await Task.WhenAny(first, second, observation);
        }
        finally
        {
            await window.CancelAsync();
        }
        await ObserveCancelledDelayAsync(observation, window);
        await Assert.That(!ReferenceEquals(completed, first)).IsTrue();
        await Assert.That(!ReferenceEquals(completed, second)).IsTrue();
    }

    internal static async Task AssertSettlementOrderAsync(NativeRequestWorkProbe probe)
    {
        await Assert.That(probe.ProducerSettleCount).IsEqualTo(1);
        await Assert.That(probe.ActivationSettleCount).IsEqualTo(1);
        await Assert.That(probe.ActivationOrder).IsGreaterThan(probe.ProducerOrder);
    }

    private static async Task ObserveCancelledDelayAsync(Task observation, CancellationTokenSource window)
    {
        try
        {
            await observation;
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested)
        {
        }
    }
}
