using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsFatalSettlementCases
{
    private static readonly TimeSpan SettlementBound = TimeSpan.FromSeconds(10);
    private const string CleanupCanary = "fatal-settlement-cleanup-canary";

    internal static async Task AcCrs003FatalCreateFailureSettlesActivationAndKeepsFatalPrecedence(
        RequestCqrsFatalCase fatal)
    {
        await AssertFatalAsync(fatal);
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(SettlementBound, TimeProvider.System);
        var ordinaryCleanup = new IOException(CleanupCanary);
        var stream = CqrsStreamLifetime.Run(_ => throw fatal.Thrown, requestId, RequestCqrsFatalSettlementProbe.Activation(observation, ordinaryCleanup), owner: null, cancellationToken: cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.DrainAsync(stream, requestId, observation, cancellation.Token), fatal.Fatal);
        await Assert.That(escaped).IsSameReferenceAs(fatal.Fatal);
        await AssertNoChunksAsync(observation);
        await Assert.That(observation.ProducerSettlementCount).IsEqualTo(0);
        await Assert.That(observation.ActivationSettlementCount).IsEqualTo(1);
        await AssertHealthyFollowingStreamAsync();
        await AssertPrimaryFatalWinsAsync(fatal);
    }

    internal static async Task AcCrs003PullFatalNeverBecomesFailedAndJoinsProducer(
        RequestCqrsFatalCase fatal)
    {
        await AssertFatalAsync(fatal);
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(SettlementBound, TimeProvider.System);
        var stream = CqrsStreamLifetime.Run(token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => RequestCqrsFatalSettlementProbe.ThrowAfterStartedAsync(
                    writer, requestId, fatal.Thrown, observation), token), requestId, RequestCqrsFatalSettlementProbe.Activation(observation), owner: null, cancellationToken: cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.DrainFatalPullAsync(stream, requestId, observation, cancellation.Token), fatal.Fatal);
        await Assert.That(escaped).IsSameReferenceAs(fatal.Fatal);
        await AssertObservedStartedOnlyAsync(observation, requestId);
        await AssertSettledOnceAsync(observation);
        await AssertHealthyFollowingStreamAsync();
    }

    internal static async Task AcCrs003DisposalFatalSettlesBeforeActivation(
        RequestCqrsFatalCase fatal)
    {
        await AssertFatalAsync(fatal);
        await AssertDisposalFatalAsync(fatal, new IOException(CleanupCanary));
        await AssertDisposalFatalAsync(
            fatal, RequestCqrsFatalSettlementProbe.DifferentFatal(fatal.Fatal));
    }

    internal static async Task AcCrs003ActivationFatalAndOrdinaryCleanupFaultsRemainObservable(
        RequestCqrsFatalCase fatal)
    {
        await AssertFatalAsync(fatal);
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(SettlementBound, TimeProvider.System);
        var stream = CqrsStreamLifetime.Run(token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => RequestCqrsFatalSettlementProbe.CompleteAsync(writer, requestId, observation), token), requestId, RequestCqrsFatalSettlementProbe.Activation(observation, fatal.Thrown), owner: null, cancellationToken: cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.DrainCompletedAsync(stream, requestId, observation, cancellation.Token), fatal.Fatal);
        await Assert.That(escaped).IsSameReferenceAs(fatal.Fatal);
        await AssertObservedCompletedPairAsync(observation, requestId);
        await AssertSettledOnceAsync(observation);
        await AssertHealthyFollowingStreamAsync();
    }

    private static async Task AssertPrimaryFatalWinsAsync(
        RequestCqrsFatalCase primaryFatal)
    {
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        var activationFatal = RequestCqrsFatalSettlementProbe.DifferentFatal(primaryFatal.Fatal);
        using var cancellation = new CancellationTokenSource(SettlementBound, TimeProvider.System);
        var stream = CqrsStreamLifetime.Run(_ => throw primaryFatal.Thrown, requestId, RequestCqrsFatalSettlementProbe.Activation(observation, activationFatal), owner: null, cancellationToken: cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.DrainAsync(stream, requestId, observation, cancellation.Token), primaryFatal.Fatal);
        await Assert.That(escaped).IsSameReferenceAs(primaryFatal.Fatal);
        await AssertNoChunksAsync(observation);
        await Assert.That(observation.ActivationSettlementCount).IsEqualTo(1);
        await AssertHealthyFollowingStreamAsync();
    }

    private static async Task AssertDisposalFatalAsync(
        RequestCqrsFatalCase fatal, Exception activationFailure)
    {
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(SettlementBound, TimeProvider.System);
        var stream = CqrsStreamLifetime.Run(token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => RequestCqrsFatalSettlementProbe.WaitThenThrowAsync(
                    writer, requestId, fatal.Thrown, observation), token), requestId, RequestCqrsFatalSettlementProbe.Activation(observation, activationFailure), owner: null, cancellationToken: cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.CancelAndDisposeAfterStartedAsync(stream, requestId, cancellation, observation), fatal.Fatal);
        await Assert.That(escaped).IsSameReferenceAs(fatal.Fatal);
        await AssertObservedStartedOnlyAsync(observation, requestId);
        await AssertSettledOnceAsync(observation);
        await AssertHealthyFollowingStreamAsync();
    }

    internal static async Task AssertFatalActivationWinsOverOrdinaryPrimaryAsync()
    {
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        var ordinaryPrimary = new InvalidOperationException(CleanupCanary);
        var activationFatal = RequestCqrsFatalSettlementProbe.FatalCases().First().Fatal;
        using var cancellation = new CancellationTokenSource(SettlementBound, TimeProvider.System);
        var stream = CqrsStreamLifetime.Run(_ => throw ordinaryPrimary, requestId, RequestCqrsFatalSettlementProbe.Activation(observation, activationFatal), owner: null, cancellationToken: cancellation.Token);

        var escaped = await RequestCqrsFatalSettlementProbe.CaptureExpectedAsync(
            () => RequestCqrsFatalStreamDrain.DrainAsync(stream, requestId, observation, cancellation.Token), activationFatal);
        await Assert.That(escaped).IsSameReferenceAs(activationFatal);
        await AssertNoChunksAsync(observation);
        await Assert.That(observation.ActivationSettlementCount).IsEqualTo(1);
        await AssertHealthyFollowingStreamAsync();
    }

    internal static async Task AssertOrdinaryCleanupFailuresRemainObservable()
    {
        var observation = new RequestCqrsFatalObservation();
        var requestId = Guid.NewGuid();
        var primary = new InvalidOperationException(CleanupCanary);
        var cleanup = new IOException(CleanupCanary);
        using var cancellation = new CancellationTokenSource(SettlementBound, TimeProvider.System);
        var stream = CqrsStreamLifetime.Run(_ => throw primary, requestId, RequestCqrsFatalSettlementProbe.Activation(observation, cleanup), owner: null, cancellationToken: cancellation.Token);

        var failure = (await Assert.ThrowsExactlyAsync<AggregateException>(
            () => RequestCqrsFatalStreamDrain.DrainAsync(stream, requestId, observation, cancellation.Token)))!;
        await Assert.That(failure.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(failure.InnerExceptions[0]).IsSameReferenceAs(primary);
        await Assert.That(failure.InnerExceptions[1]).IsSameReferenceAs(cleanup);
        await AssertNoChunksAsync(observation);
        await Assert.That(observation.ActivationSettlementCount).IsEqualTo(1);
        await AssertHealthyFollowingStreamAsync();
    }

    private static async Task AssertFatalAsync(RequestCqrsFatalCase fatal)
    {
        await Assert.That(CqrsRuntimeFailures.FindFatal(fatal.Thrown)).IsSameReferenceAs(fatal.Fatal);
        await Assert.That(CqrsBoundaryErrors.IsNonFatal(fatal.Thrown)).IsFalse();
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

    private static async Task AssertHealthyFollowingStreamAsync()
    {
        var requestId = Guid.NewGuid();
        var observation = new RequestCqrsFatalObservation();
        using var cancellation = new CancellationTokenSource(SettlementBound, TimeProvider.System);
        var stream = CqrsStreamLifetime.Run(token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => RequestCqrsFatalSettlementProbe.CompleteAsync(writer, requestId, observation), token), requestId, RequestCqrsFatalSettlementProbe.Activation(observation), owner: null, cancellationToken: cancellation.Token);
        await RequestCqrsFatalStreamDrain.DrainCompletedAsync(stream, requestId, observation, cancellation.Token);
        await AssertSettledOnceAsync(observation);
    }
}
