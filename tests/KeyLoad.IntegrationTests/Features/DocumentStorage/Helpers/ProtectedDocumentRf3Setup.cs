using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Every setup effect is the real configured receiver operation and RF3 journal; no stores are edited.</summary>
internal sealed class ProtectedDocumentRf3Setup(ProtectedDocumentRf3SetupPeer peer,
    PartitionRef partition, AtomicPartitionPlacementResolution placement)
{
    private const string Administrator = "root";
    private readonly Guid move = Guid.NewGuid();
    private PartitionMoveControlRecord? control;
    private string? digest;
    private DateTimeOffset Expiry => peer.Timing.SetupExpiry;

    internal async Task<PartitionMovePhaseResult> ExecuteAsync(CancellationToken token)
    {
        var body = NativeSerialization.Serialize(new PartitionMovePrepareBody(Administrator,
            new PartitionMoveRequest(move, partition, peer.Target.PhysicalShardId, placement.Revision, PartitionMoveMode.Transfer)));
        digest = Convert.ToHexStringLower(SHA256.HashData(body));
        var prepared = await LocalEncodedAsync(PartitionMovePeerStage.ControlPrepare, body, token).ConfigureAwait(false);
        control = RequireControl(prepared);
        var fenceEffect = await EffectAsync(PartitionMovePeerStage.Fence,
            new PartitionMoveControlBody(Administrator, control), false, 0, [], token).ConfigureAwait(false);
        var fence = fenceEffect.Result.Fence ?? throw new InvalidDataException(PartitionMoveProtocol.MissingAuthority);
        var fenced = await LocalAsync(PartitionMovePeerStage.ControlAcceptFence,
            new PartitionMoveFenceAcceptBody(Administrator, control, fence, fenceEffect.GrantId), token).ConfigureAwait(false);
        control = RequireControl(fenced);
        var capture = await CaptureAsync(fence, token).ConfigureAwait(false);
        var captured = await LocalAsync(PartitionMovePeerStage.ControlAdvance,
            new PartitionMoveAdvanceBody(Administrator, control, fence, capture.Handle.Descriptor, null,
                SourceCaptureGrantId: capture.GrantId), token).ConfigureAwait(false);
        control = RequireControl(captured);
        foreach (var page in capture.Pages)
        {
            var native = NativeSerialization.Deserialize<PartitionMoveImagePage>(page.NativePage.Span);
            _ = await EffectAsync(PartitionMovePeerStage.StagePage,
                new PartitionMovePageBody(Administrator, control, fence, capture.Handle.Descriptor, native),
                true, page.Ordinal, [], token).ConfigureAwait(false);
        }
        (PartitionMovePhaseResult Result, Guid GrantId) installed = default;
        for (var ordinal = 0; ordinal <= capture.Handle.PageCount; ordinal++)
        {
            installed = await EffectAsync(PartitionMovePeerStage.Install,
                new PartitionMoveInstallBody(Administrator, control, fence, capture.Handle.Descriptor),
                true, ordinal, [], token).ConfigureAwait(false);
        }
        var installedResult = installed.Result ?? throw new InvalidDataException(PartitionMoveProtocol.MissingAuthority);
        var receipt = installedResult.InstalledReceipt ?? throw new InvalidDataException(PartitionMoveProtocol.MissingAuthority);
        control = RequireControl(await LocalAsync(PartitionMovePeerStage.ControlAdvance,
            new PartitionMoveAdvanceBody(Administrator, control, fence, capture.Handle.Descriptor,
                receipt, TargetInstallGrantId: installed.GrantId), token).ConfigureAwait(false));
        var finalized = await LocalAsync(PartitionMovePeerStage.ControlFinalize,
            new PartitionMoveControlBody(Administrator, control), token).ConfigureAwait(false);
        control = RequireControl(finalized);
        var publication = new PartitionMovePublishedPlacement(control.Version, move,
            finalized.PublishedPlacement ?? throw new InvalidDataException(PartitionMoveProtocol.MissingAuthority),
            placement, peer.Target, receipt.Token, finalized.Journal);
        var published = await EffectAsync(PartitionMovePeerStage.PublishWitness,
            new PartitionMovePublishBody(Administrator, control, publication, capture.Handle.Descriptor.Resources),
            true, 0, capture.Handle.Descriptor.Resources, token).ConfigureAwait(false);
        (PartitionMovePhaseResult Result, Guid GrantId) retired = default;
        ReadOnlyMemory<byte> originalRetirement = default;
        for (var ordinal = 0; ordinal <= PartitionMoveCleanupFamilies.All.Length; ordinal++)
        {
            originalRetirement = NativeSerialization.Serialize(new PartitionMoveCleanupBody(Administrator,
                control, PartitionMoveCleanupRole.Source, ordinal, published.GrantId, publication));
            retired = await EffectEncodedAsync(PartitionMovePeerStage.Retire, originalRetirement,
                false, ordinal, [], token).ConfigureAwait(false);
        }
        var retiredResult = retired.Result ?? throw new InvalidDataException(PartitionMoveProtocol.MissingAuthority);
        var complete = await LocalAsync(PartitionMovePeerStage.ControlCompleteRetirement,
            new PartitionMoveCompletionBody(Administrator, control, retiredResult.Journal, null,
                retired.GrantId, null, originalRetirement, ReadOnlyMemory<byte>.Empty), token).ConfigureAwait(false);
        await Assert.That(RequireControl(complete).Phase).IsEqualTo(PartitionMovePhase.Retired);
        return complete;
    }

    private async Task<(PartitionMovementCaptureHandle Handle, PartitionMovementPageResult[] Pages, Guid GrantId)>
        CaptureAsync(PartitionMoveSourceFenceRecord fence, CancellationToken token)
    {
        var body = NativeSerialization.Serialize(new PartitionMoveCaptureRequest(Administrator, fence,
            MaximumPageBytes: 65536, MaximumImageBytes: 262144, MaximumRecords: 256));
        var originalExpiry = Expiry;
        var authorized = await AuthorizeAsync(PartitionMovePeerStage.Capture, body, false, 0, [], originalExpiry, token).ConfigureAwait(false);
        var request = peer.Proposal(authorized.Grant.PhaseCommandId,
            Envelope(PartitionMovePeerStage.Capture, body, 0, originalExpiry, authorized.Grant), authorized.Journal,
            PartitionMovementTransportAction.Capture);
        var handle = await peer.SendAsync<PartitionMovementCaptureHandle>(request, false, token).ConfigureAwait(false);
        var failures = new List<Exception>();
        var pages = new PartitionMovementPageResult[handle.PageCount];
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            for (var ordinal = 0; ordinal < pages.Length; ordinal++)
            {
                pages[ordinal] = await peer.SendAsync<PartitionMovementPageResult>(request with
                {
                    Envelope = request.Envelope with { Nonce = Guid.NewGuid() },
                    Action = PartitionMovementTransportAction.Page,
                    HandleId = handle.HandleId,
                    Ordinal = ordinal
                }, false, token).ConfigureAwait(false);
                await Assert.That(pages[ordinal].HandleId).IsEqualTo(handle.HandleId);
                await Assert.That(pages[ordinal].Ordinal).IsEqualTo(ordinal);
            }
        }, failures).ConfigureAwait(false);
        using var cleanup = new CancellationTokenSource(peer.Timing.CleanupTimeout, peer.Timing.Clock);
        PartitionMovePhaseResult? settled = null;
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            settled = await peer.SendAsync<PartitionMovePhaseResult>(request with
            {
                Envelope = request.Envelope with { Nonce = Guid.NewGuid() },
                Action = PartitionMovementTransportAction.Release,
                HandleId = handle.HandleId
            }, false, cleanup.Token).ConfigureAwait(false);
            await AcknowledgeAsync(authorized.Grant.GrantId, settled.Journal, cleanup.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return (handle, pages, authorized.Grant.GrantId);
    }

    private Task<PartitionMovePhaseResult> LocalAsync<T>(PartitionMovePeerStage stage, T body, CancellationToken token)
        => LocalEncodedAsync(stage, NativeSerialization.Serialize(body), token);

    private Task<PartitionMovePhaseResult> LocalEncodedAsync(PartitionMovePeerStage stage,
        ReadOnlyMemory<byte> body, CancellationToken token)
        => peer.SendAsync<PartitionMovePhaseResult>(peer.Proposal(Guid.NewGuid(), Envelope(stage, body, 0, Expiry)), false, token);

    private Task<(PartitionMovePhaseResult Result, Guid GrantId)> EffectAsync<T>(PartitionMovePeerStage stage,
        T body, bool target, int ordinal, ImmutableArray<ResourceDefinition> resources, CancellationToken token)
        => EffectEncodedAsync(stage, NativeSerialization.Serialize(body), target, ordinal, resources, token);

    private async Task<(PartitionMovePhaseResult Result, Guid GrantId)> EffectEncodedAsync(PartitionMovePeerStage stage,
        ReadOnlyMemory<byte> body, bool target, int ordinal, ImmutableArray<ResourceDefinition> resources, CancellationToken token)
    {
        var expiry = Expiry;
        var authorized = await AuthorizeAsync(stage, body, target, ordinal, resources, expiry, token).ConfigureAwait(false);
        var result = await peer.SendAsync<PartitionMovePhaseResult>(peer.Proposal(authorized.Grant.PhaseCommandId,
            Envelope(stage, body, ordinal, expiry, authorized.Grant), authorized.Journal), target, token).ConfigureAwait(false);
        await AcknowledgeAsync(authorized.Grant.GrantId, result.Journal, token).ConfigureAwait(false);
        return (result, authorized.Grant.GrantId);
    }

    private async Task<(PartitionMovePhaseGrant Grant, PartitionMoveJournalReceipt Journal)> AuthorizeAsync(
        PartitionMovePeerStage stage, ReadOnlyMemory<byte> body, bool target, int ordinal,
        ImmutableArray<ResourceDefinition> resources, DateTimeOffset expiry, CancellationToken token)
    {
        var command = Guid.NewGuid();
        var grant = Guid.NewGuid();
        var phase = new PartitionMovePhaseCommand(1, move, partition, peer.Source, placement, peer.Target,
            digest!, stage, ordinal, body, Resources: resources);
        var envelope = Envelope(PartitionMovePeerStage.ControlAuthorize,
            NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(grant, command, Administrator,
                phase, target ? peer.Target : peer.Source, expiry)), 0, expiry);
        var result = await peer.SendAsync<PartitionMovePhaseResult>(peer.Proposal(grant, envelope), false, token).ConfigureAwait(false);
        return (result.Grant ?? throw new InvalidDataException(PartitionMoveProtocol.MissingAuthority), result.Journal);
    }

    private Task<PartitionMovePhaseResult> AcknowledgeAsync(Guid grant, PartitionMoveJournalReceipt receipt, CancellationToken token)
        => LocalAsync(PartitionMovePeerStage.ControlAcknowledge, new PartitionMoveAcknowledgeBody(grant, receipt), token);

    private PartitionMovePeerEnvelope Envelope(PartitionMovePeerStage stage, ReadOnlyMemory<byte> body,
        int ordinal, DateTimeOffset expiry, PartitionMovePhaseGrant? grant = null)
        => new(1, move, partition, peer.Source, placement, peer.Target, digest!, stage, ordinal, expiry, Guid.NewGuid(), body, grant);

    private static PartitionMoveControlRecord RequireControl(PartitionMovePhaseResult result)
        => result.Control ?? throw new InvalidDataException(PartitionMoveProtocol.MissingAuthority);
}
