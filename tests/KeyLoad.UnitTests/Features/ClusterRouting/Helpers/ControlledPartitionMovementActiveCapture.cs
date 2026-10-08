using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Keeps the genuine original capture owner alive through the actual abort operation without settling Release.</summary>
internal static class ControlledPartitionMovementActiveCapture
{
    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source, ServerRuntimeOptions runtime,
        PartitionMovementPeerAdmission admission, ControlledPartitionMovementLoopbackCorpus corpus,
        PartitionMovePhaseResult accepted, string callerAddress, DateTimeOffset expiry,
        Func<ControlledPartitionMovementCaptureRuntime, PartitionMovementTransportRequest,
            PartitionMovementCaptureHandle, Task> operation, CancellationToken cancellationToken)
    {
        var authorization = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            ControlledPartitionMovementCaptureRequest.Authorize(source.Database, runtime, accepted, corpus,
                callerAddress, expiry), cancellationToken);
        await Assert.That(authorization.Error).IsNull();
        var request = ControlledPartitionMovementCaptureRequest.Capture(source.Database, runtime, accepted,
            authorization.Get<PartitionMovePhaseResult>(), corpus, callerAddress, expiry);
        var verified = await ControlledPartitionMovementPeerVerification.VerifyAsync(source, runtime, admission,
            request, cancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new ControlledPartitionMovementCaptureRuntime(source, runtime,
                new(source, verified));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var original = ControlledPartitionMovementRawImage.Bytes(source.Store);
                var position = source.Store.Position;
                var index = source.Journal.Log.State.LastIndex;
                var handle = await owner.Source.CaptureAsync(
                    ControlledPartitionMovementCaptureFlow.Principal(source, verified), verified.Envelope,
                    cancellationToken);
                await ControlledPartitionMovementResourceAssertions.CaptureAsync(handle.Descriptor);
                await Assert.That(handle.ExpiresAt).IsEqualTo(expiry);
                var pages = await ControlledPartitionMovementCapturedPageReader.ReadAsync(source, owner.Source,
                    runtime, admission, handle, request, cancellationToken);
                await ControlledPartitionMovementCapturedPages.AssertAsync(original, handle, pages);
                await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
                    .SequenceEqual(original, StringComparer.Ordinal)).IsTrue();
                await Assert.That(source.Store.Position).IsEqualTo(position);
                await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
                await operation(owner, verified, handle);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
