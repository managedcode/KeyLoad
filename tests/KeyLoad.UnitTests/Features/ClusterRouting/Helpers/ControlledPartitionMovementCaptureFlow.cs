using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Uses only genuine authenticated capture capabilities and joins release before returning its logged result.</summary>
internal static class ControlledPartitionMovementCaptureFlow
{
    internal static async Task<(PartitionMovementCaptureHandle Handle, PartitionMovementPageResult[] Pages,
        PartitionMovePhaseResult Settlement)> ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult acceptedFence,
        string callerAddress, DateTimeOffset originalExpiry, CancellationToken cancellationToken)
    {
        var authorization = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            ControlledPartitionMovementCaptureRequest.Authorize(source.Database, runtime, acceptedFence, corpus,
                callerAddress, originalExpiry), cancellationToken);
        await Assert.That(authorization.Error).IsNull();
        var request = ControlledPartitionMovementCaptureRequest.Capture(source.Database, runtime, acceptedFence,
            authorization.Get<PartitionMovePhaseResult>(), corpus, callerAddress, originalExpiry);
        var verified = await ControlledPartitionMovementPeerVerification.VerifyAsync(source, runtime, admission,
            request, cancellationToken);
        var failures = new List<Exception>();
        PartitionMovementCaptureHandle? handle = null;
        PartitionMovementPageResult[]? pages = null;
        PartitionMovePhaseResult? settlement = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new ControlledPartitionMovementCaptureRuntime(source, runtime,
                new(source, verified));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var sourceBeforeCapture = ControlledPartitionMovementRawImage.Bytes(source.Store);
                var sourcePosition = source.Store.Position;
                var sourceIndex = source.Journal.Log.State.LastIndex;
                handle = await owner.Source.CaptureAsync(Principal(source, verified), verified.Envelope,
                    cancellationToken);
                await ControlledPartitionMovementResourceAssertions.CaptureAsync(handle.Descriptor);
                await Assert.That(handle.ExpiresAt).IsEqualTo(originalExpiry);
                pages = await ControlledPartitionMovementCapturedPageReader.ReadAsync(source, owner.Source,
                    runtime, admission, handle, request, cancellationToken);
                await ControlledPartitionMovementCapturedPages.AssertAsync(sourceBeforeCapture, handle, pages);
                await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
                    .SequenceEqual(sourceBeforeCapture, StringComparer.Ordinal)).IsTrue();
                await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
                await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(sourceIndex);
                var release = await ControlledPartitionMovementPeerVerification.VerifyAsync(source, runtime, admission,
                    request with
                    {
                        Action = PartitionMovementTransportAction.Release,
                        HandleId = handle.HandleId,
                        Envelope = request.Envelope with { Nonce = Guid.NewGuid() }
                    }, cancellationToken);
                settlement = await owner.Source.ReleaseAsync(Principal(source, release), release.Envelope,
                    handle.HandleId, cancellationToken);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return (handle ?? throw new InvalidOperationException("No actual capture returned."),
            pages ?? throw new InvalidOperationException("No actual native pages returned."),
            settlement ?? throw new InvalidOperationException("No actual release settlement returned."));
    }

    internal static PrincipalRecord Principal(ControlledPartitionMovementNode source,
        PartitionMovementTransportRequest verified) => source.Store.Read(view => source.Database.Principal(view,
            PartitionMovementControlPrincipal.Resolve(source.Database, verified.Envelope),
            source.Database.EvaluationClock.GetUtcNow()));
}
