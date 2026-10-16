using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportCapture
{
    internal static async Task<D2ImportImage> ExecuteAsync(D2ImportSession session,
        string partitionKey, CancellationToken token)
    {
        var accepted = await D2ImportCapturePreparation.ExecuteAsync(session,
            D2ImportCrashProtocol.Partition(partitionKey), token);
        var phase = D2ImportPhases.Phase(session, accepted, PartitionMovePeerStage.Capture,
            D2ImportCaptureRequest.Body(session, accepted));
        var issued = await D2ImportPhases.AuthorizeAsync(session, accepted, phase, atSource: true, token,
            PartitionMovementTransportAction.Capture);
        var verified = await D2ImportCaptureSettlement.VerifyAsync(session, issued.Request, token);
        var captured = await ReadAsync(session, verified, token);
        await D2ImportPhases.AcknowledgeAsync(session, accepted, issued.GrantId, captured.Settlement, token);
        var advanced = await D2ImportPhases.LocalAsync(session, D2ImportPhases.Phase(session, accepted,
            PartitionMovePeerStage.ControlAdvance, NativeSerialization.Serialize(new PartitionMoveAdvanceBody(
                D2ImportCrashProtocol.Principal, accepted.Control!, accepted.Fence!, captured.Handle.Descriptor,
                null, issued.GrantId))), token);
        return new(advanced, accepted.Fence!, captured.Handle, captured.Pages);
    }

    private static async Task<(PartitionMovementCaptureHandle Handle, PartitionMovementPageResult[] Pages,
        PartitionMovePhaseResult Settlement)> ReadAsync(D2ImportSession session,
        PartitionMovementTransportRequest request, CancellationToken token)
    {
        var failures = new List<Exception>();
        PartitionMovementCaptureHandle? handle = null;
        PartitionMovementPageResult[]? pages = null;
        PartitionMovePhaseResult? settlement = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var runtime = new D2ImportCaptureRuntime(session, request);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                handle = await runtime.Source.CaptureAsync(D2ImportCaptureSettlement.Principal(session, request),
                    request.Envelope, token);
                pages = await PagesAsync(session, runtime.Source, request, handle, token);
                var release = await D2ImportCaptureSettlement.VerifyAsync(session, request with
                {
                    Action = PartitionMovementTransportAction.Release,
                    HandleId = handle.HandleId,
                    Envelope = request.Envelope with
                    {
                        Nonce = Guid.NewGuid()
                    }
                }, token);
                settlement = await runtime.Source.ReleaseAsync(D2ImportCaptureSettlement.Principal(session, release),
                    release.Envelope, handle.HandleId, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return (handle!, pages!, settlement!);
    }

    private static async Task<PartitionMovementPageResult[]> PagesAsync(D2ImportSession session,
        PartitionMovementSourceOwner owner, PartitionMovementTransportRequest request,
        PartitionMovementCaptureHandle handle, CancellationToken token)
    {
        await Assert.That(handle.PageCount).IsGreaterThan(D2ImportCrashProtocol.Empty);
        await Assert.That(handle.ExpiresAt).IsEqualTo(session.ExpiresAt);
        var pages = new PartitionMovementPageResult[handle.PageCount];
        for (var ordinal = D2ImportCrashProtocol.Empty; ordinal < pages.Length; ordinal++)
        {
            var page = await D2ImportCaptureSettlement.VerifyAsync(session, request with
            {
                Action = PartitionMovementTransportAction.Page,
                HandleId = handle.HandleId,
                Ordinal = ordinal,
                Envelope = request.Envelope with
                {
                    Nonce = Guid.NewGuid()
                }
            }, token);
            pages[ordinal] = await owner.ReadPageAsync(D2ImportCaptureSettlement.Principal(session, page),
                page.Envelope, new(handle.HandleId, ordinal), token);
            await Assert.That(pages[ordinal].HandleId).IsEqualTo(handle.HandleId);
            await Assert.That(pages[ordinal].Ordinal).IsEqualTo(ordinal);
        }
        return pages;
    }
}
