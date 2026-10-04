using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsFatalSettlementCases
{
    private static readonly TimeSpan SettlementBound = TimeSpan.FromSeconds(10);
    private const string CleanupCanary = "fatal-settlement-cleanup-canary";

    internal static async Task AcCrs003FatalCreateFailureSettlesActivationAndKeepsFatalPrecedence(
        RequestCqrsClusterFixture fixture, RequestCqrsFatalCase fatal)
    {
        await AssertNativeFatalAsync(fatal);
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(SettlementBound);
        var ordinaryCleanup = new IOException(CleanupCanary);
        var stream = NativeCqrsStreamLifetime.Run(
            _ => throw fatal.Thrown,
            ChunkSerializer(fixture), requestId, TimeProvider.System,
            RequestCqrsFatalSettlementProbe.Activation(observation, ordinaryCleanup), cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.DrainAsync(stream, requestId, observation, cancellation.Token), fatal.Fatal);
        await Assert.That(escaped).IsSameReferenceAs(fatal.Fatal);
        await AssertNoChunksAsync(observation);
        await Assert.That(observation.ProducerSettlementCount).IsEqualTo(0);
        await Assert.That(observation.ActivationSettlementCount).IsEqualTo(1);
        await AssertHealthyFollowingStreamAsync(fixture);
        await AssertPrimaryFatalWinsAsync(fixture, fatal);
    }

    internal static async Task AcCrs003NativePullFatalNeverBecomesFailedAndJoinsProducer(
        RequestCqrsClusterFixture fixture, RequestCqrsFatalCase fatal)
    {
        await AssertNativeFatalAsync(fatal);
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(SettlementBound);
        var stream = NativeCqrsStreamLifetime.Run(
            token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => RequestCqrsFatalSettlementProbe.ThrowAfterStartedAsync(
                    writer, requestId, fatal.Thrown, observation), token),
            ChunkSerializer(fixture), requestId, TimeProvider.System,
            RequestCqrsFatalSettlementProbe.Activation(observation), cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.DrainFatalPullAsync(stream, requestId, observation, cancellation.Token), fatal.Fatal);
        await Assert.That(escaped).IsSameReferenceAs(fatal.Fatal);
        await AssertObservedStartedOnlyAsync(observation, requestId);
        await AssertSettledOnceAsync(observation);
        await AssertHealthyFollowingStreamAsync(fixture);
    }

    internal static async Task AcCrs003NativeDisposalFatalSettlesBeforeActivation(
        RequestCqrsClusterFixture fixture, RequestCqrsFatalCase fatal)
    {
        await AssertNativeFatalAsync(fatal);
        await AssertDisposalFatalAsync(fixture, fatal, new IOException(CleanupCanary));
        await AssertDisposalFatalAsync(
            fixture, fatal, RequestCqrsFatalSettlementProbe.DifferentFatal(fatal.Fatal));
    }

    internal static async Task AcCrs003ActivationFatalAndOrdinaryCleanupFaultsRemainObservable(
        RequestCqrsClusterFixture fixture, RequestCqrsFatalCase fatal)
    {
        await AssertNativeFatalAsync(fatal);
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(SettlementBound);
        var stream = NativeCqrsStreamLifetime.Run(
            token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => RequestCqrsFatalSettlementProbe.CompleteAsync(writer, requestId, observation), token),
            ChunkSerializer(fixture), requestId, TimeProvider.System,
            RequestCqrsFatalSettlementProbe.Activation(observation, fatal.Thrown), cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.DrainCompletedAsync(stream, requestId, observation, cancellation.Token), fatal.Fatal);
        await Assert.That(escaped).IsSameReferenceAs(fatal.Fatal);
        await AssertObservedCompletedPairAsync(observation, requestId);
        await AssertSettledOnceAsync(observation);
        await AssertHealthyFollowingStreamAsync(fixture);
    }

    private static async Task AssertPrimaryFatalWinsAsync(
        RequestCqrsClusterFixture fixture, RequestCqrsFatalCase primaryFatal)
    {
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        var activationFatal = RequestCqrsFatalSettlementProbe.DifferentFatal(primaryFatal.Fatal);
        using var cancellation = new CancellationTokenSource(SettlementBound);
        var stream = NativeCqrsStreamLifetime.Run(
            _ => throw primaryFatal.Thrown,
            ChunkSerializer(fixture), requestId, TimeProvider.System,
            RequestCqrsFatalSettlementProbe.Activation(observation, activationFatal), cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.DrainAsync(stream, requestId, observation, cancellation.Token), primaryFatal.Fatal);
        await Assert.That(escaped).IsSameReferenceAs(primaryFatal.Fatal);
        await AssertNoChunksAsync(observation);
        await Assert.That(observation.ActivationSettlementCount).IsEqualTo(1);
        await AssertHealthyFollowingStreamAsync(fixture);
    }

    private static async Task AssertDisposalFatalAsync(
        RequestCqrsClusterFixture fixture, RequestCqrsFatalCase fatal, Exception activationFailure)
    {
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(SettlementBound);
        var stream = NativeCqrsStreamLifetime.Run(
            token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => RequestCqrsFatalSettlementProbe.WaitThenThrowAsync(
                    writer, requestId, fatal.Thrown, observation), token),
            ChunkSerializer(fixture), requestId, TimeProvider.System,
            RequestCqrsFatalSettlementProbe.Activation(observation, activationFailure), cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.CancelAndDisposeAfterStartedAsync(stream, requestId, cancellation, observation), fatal.Fatal);
        await Assert.That(escaped).IsSameReferenceAs(fatal.Fatal);
        await AssertObservedStartedOnlyAsync(observation, requestId);
        await AssertSettledOnceAsync(observation);
        await AssertHealthyFollowingStreamAsync(fixture);
    }

    internal static async Task AssertFatalActivationWinsOverOrdinaryPrimaryAsync(RequestCqrsClusterFixture fixture)
    {
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        var ordinaryPrimary = new InvalidOperationException(CleanupCanary);
        var activationFatal = RequestCqrsFatalSettlementProbe.FatalCases().First().Fatal;
        using var cancellation = new CancellationTokenSource(SettlementBound);
        var stream = NativeCqrsStreamLifetime.Run(
            _ => throw ordinaryPrimary,
            ChunkSerializer(fixture), requestId, TimeProvider.System,
            RequestCqrsFatalSettlementProbe.Activation(observation, activationFatal), cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.DrainAsync(stream, requestId, observation, cancellation.Token), activationFatal);
        await Assert.That(escaped).IsSameReferenceAs(activationFatal);
        await AssertNoChunksAsync(observation);
        await Assert.That(observation.ActivationSettlementCount).IsEqualTo(1);
        await AssertHealthyFollowingStreamAsync(fixture);
    }

    internal static async Task AssertOrdinaryCleanupFailuresRemainObservable(RequestCqrsClusterFixture fixture)
    {
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        var primary = new InvalidOperationException(CleanupCanary);
        var cleanup = new IOException(CleanupCanary);
        using var cancellation = new CancellationTokenSource(SettlementBound);
        var stream = NativeCqrsStreamLifetime.Run(
            _ => throw primary,
            ChunkSerializer(fixture), requestId, TimeProvider.System,
            RequestCqrsFatalSettlementProbe.Activation(observation, cleanup), cancellation.Token);

        var failure = (await Assert.ThrowsExactlyAsync<AggregateException>(
            () => RequestCqrsFatalStreamDrain.DrainAsync(stream, requestId, observation, cancellation.Token)))!;
        await Assert.That(failure.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(failure.InnerExceptions[0]).IsSameReferenceAs(primary);
        await Assert.That(failure.InnerExceptions[1]).IsSameReferenceAs(cleanup);
        await AssertNoChunksAsync(observation);
        await Assert.That(observation.ActivationSettlementCount).IsEqualTo(1);
        await AssertHealthyFollowingStreamAsync(fixture);
    }

    private static async Task AssertNativeFatalAsync(RequestCqrsFatalCase fatal)
    {
        await Assert.That(CqrsRuntimeFailures.FindFatal(fatal.Thrown)).IsSameReferenceAs(fatal.Fatal);
        await Assert.That(NativeCqrsBoundaryErrors.IsNonFatal(fatal.Thrown)).IsFalse();
    }

    private static async Task AssertNoChunksAsync(RequestCqrsFatalObservation observation)
        => await Assert.That(observation.ObservedChunkCount).IsEqualTo(0);

    private static async Task AssertObservedStartedOnlyAsync(
        RequestCqrsFatalObservation observation, Guid requestId)
    {
        await Assert.That(observation.ObservedChunkCount).IsEqualTo(1);
        await Assert.That(observation.ObservedChunkKind(0)).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(observation.ObservedRequestId(0)).IsEqualTo(requestId);
    }

    private static async Task AssertObservedCompletedPairAsync(
        RequestCqrsFatalObservation observation, Guid requestId)
    {
        await Assert.That(observation.ObservedChunkCount).IsEqualTo(2);
        await Assert.That(observation.ObservedChunkKind(0)).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(observation.ObservedChunkKind(1)).IsEqualTo(CqrsStreamChunkKind.Completed);
        await Assert.That(observation.ObservedRequestId(0)).IsEqualTo(requestId);
    }

    private static async Task AssertSettledOnceAsync(RequestCqrsFatalObservation observation)
    {
        await Assert.That(observation.ProducerSettlementCount).IsEqualTo(1);
        await Assert.That(observation.ActivationSettlementCount).IsEqualTo(1);
        await Assert.That(observation.ActivationSettlementOrder).IsGreaterThan(observation.ProducerSettlementOrder);
    }

    private static Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ChunkSerializer(
        RequestCqrsClusterFixture fixture)
        => fixture.Cluster.ServiceProvider.GetRequiredService<
            Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();

    private static async Task AssertHealthyFollowingStreamAsync(RequestCqrsClusterFixture fixture)
    {
        var requestId = Guid.NewGuid();
        var observation = new RequestCqrsFatalObservation();
        using var cancellation = new CancellationTokenSource(SettlementBound);
        var stream = NativeCqrsStreamLifetime.Run(
            token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => RequestCqrsFatalSettlementProbe.CompleteAsync(writer, requestId, observation), token),
            ChunkSerializer(fixture), requestId, TimeProvider.System,
            RequestCqrsFatalSettlementProbe.Activation(observation), cancellation.Token);
        await RequestCqrsFatalStreamDrain.DrainCompletedAsync(stream, requestId, observation, cancellation.Token);
        await AssertSettledOnceAsync(observation);
    }
}
