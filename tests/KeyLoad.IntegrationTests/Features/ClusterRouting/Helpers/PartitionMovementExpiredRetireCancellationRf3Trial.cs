using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Never derives authority from a timeout: retained native phase and own cancellation outcome decide recovery.</summary>
internal sealed class PartitionMovementExpiredRetireCancellationRf3Trial
{
    private const string Administrator = "root";
    private const string MissingNativePhase = "The original native expired Retire phase is absent.";
    private readonly List<Exception> failures = [];
    private readonly List<CancellationTokenSource> callers = [];
    private readonly List<Task<Result<PartitionMoveResult>>> calls = [];
    private TwoRf3MembershipWave? wave;
    private PartitionMovementPublicParentRf3Seed? seed;
    private ReplicaSiloDiscovery[]? discovery;

    internal static async Task RunAsync(RequestCqrsProbePhase cancellationPhase, CancellationToken cancellationToken, bool corruptDisposition = false)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var trial = new PartitionMovementExpiredRetireCancellationRf3Trial();
        await ServerFailureObserver.ObserveAsync(() => trial.ExecuteAsync(cancellationPhase, corruptDisposition, parent.Token), trial.failures);
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        foreach (var caller in trial.callers)
        { await ServerFailureObserver.ObserveAsync(caller.CancelAsync, trial.failures); }
        if (trial.wave is { } owned && trial.discovery is { } signed)
        { await ServerFailureObserver.ObserveAsync(() => owned.QueryControls.ReleaseOpenArmsAsync(signed, cleanup.Token), trial.failures); }
        foreach (var call in trial.calls)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await call; }, trial.failures); }
        if (trial.seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), trial.failures); }
        if (trial.wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), trial.failures); }
        foreach (var caller in trial.callers)
        { ServerFailureObserver.Observe(caller.Dispose, trial.failures); }
        ServerFailureObserver.ThrowIfAny(trial.failures);
    }

    private async Task ExecuteAsync(RequestCqrsProbePhase cancellationPhase, bool corruptDisposition, CancellationToken cancellationToken)
    {
        wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(cancellationToken);
        seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, cancellationToken);
        discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave, cancellationToken);
        var originalId = PartitionMovementParentPhaseIds.For(seed.FirstRequest, Administrator,
            PartitionMovementParentPhaseRole.Retire);
        await InterruptAsync(originalId, RequestCqrsProbePhase.BeforeSubmit, cancellationToken);
        var first = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, originalId, cancellationToken);
        var original = RequirePending(first, originalId);
        await Assert.That(original.OriginalResult).IsNull();
        await Assert.That(first.All(cut => cut.OriginalNativeResult is null)).IsTrue();
        await Assert.That(first.Count(cut => cut.OriginalReceiverIssuance is not null)).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        await Assert.That(original.RetireCancellationAttempt).IsNull();
        await Assert.That(original.OriginalReceiverIssuePacket).IsNotNull();
        await Assert.That(original.OriginalAuthorization).IsNotNull();
        await Assert.That(original.OriginalGrant).IsNotNull();
        await PartitionMovementRetireGrantDispositionRf3Trial.RequirePendingAsync(first, original);
        var remaining = original.OriginalExpiresAt - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        { await Task.Delay(remaining, TimeProvider.System, cancellationToken); }
        await Assert.That(original.OriginalExpiresAt <= TimeProvider.System.GetUtcNow()).IsTrue();
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave, cancellationToken);
        var cancellationId = PartitionMovementParentPhaseIds.For(seed.FirstRequest, Administrator,
            PartitionMovementParentPhaseRole.RetireCancellation, original.PageOrdinal, original.CleanupGeneration);
        await InterruptAsync(cancellationId, cancellationPhase, cancellationToken);
        var interrupted = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, originalId, cancellationToken);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave, cancellationToken);
        if (cancellationPhase == RequestCqrsProbePhase.BeforeSubmit)
        { await RequireUnknownAsync(original, interrupted, cancellationToken); }
        else
        { await RequireSettledAsync(original, cancellationId, interrupted, corruptDisposition, cancellationToken); }
        await seed.VerifyAsync(cancellationToken);
    }

    private async Task InterruptAsync(Guid commandId, RequestCqrsProbePhase phase, CancellationToken cancellationToken)
    {
        var owned = wave ?? throw new InvalidOperationException(MissingNativePhase);
        var clients = seed ?? throw new InvalidOperationException(MissingNativePhase);
        var signed = discovery ?? throw new InvalidOperationException(MissingNativePhase);
        var arm = owned.QueryControls.WriteArm(Administrator, commandId, null, phase, RequestCqrsProbeAction.Hold);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callers.Add(cancellation);
        var call = clients.Source.MovePartitionAsync(clients.FirstRequest, cancellation.Token);
        calls.Add(call);
        var marker = await owned.QueryControls.WaitForMarkerAsync(arm, phase,
            RequestCqrsProbeOutcome.Observed, signed, cancellationToken);
        await Assert.That(marker.CommandId).IsEqualTo(commandId);
        await cancellation.CancelAsync();
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(owned.QueryControls, arm, marker.RequestId,
            commandId, signed, cancellationToken);
        var actual = await call;
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(ErrorCode.UnknownWriteOutcome.ToString());
        await owned.QueryControls.RetireArmAsync(arm, cancellationToken);
    }

    private async Task RequireUnknownAsync(PartitionMoveParentPhase original,
        PartitionMovementPublicParentRf3NativeCut[] before, CancellationToken cancellationToken)
    {
        var owned = wave!;
        var clients = seed!;
        var retained = RequirePending(before, original.OriginalPhaseCommandId);
        await PartitionMovementRetireGrantDispositionRf3Trial.RequirePendingAsync(before, original);
        await RequireOriginalAsync(original, retained);
        await Assert.That(retained.RetireCancellation).IsNull();
        await Assert.That(retained.RetireCancellationAttempt).IsNotNull();
        await Assert.That(before.All(cut => cut.RetireCancellation is null && cut.NativeCancellationResult is null)).IsTrue();
        var attempt = retained.RetireCancellationAttempt!;
        await Assert.That(attempt.CancellationCommandId).IsNotEqualTo(original.OriginalPhaseCommandId);
        await Assert.That(attempt.OriginalRequestBytes.IsEmpty).IsFalse();
        var header = before.First(cut => cut.Pending is not null).Header!;
        var refusal = await clients.Source.MovePartitionAsync(clients.FirstRequest, cancellationToken);
        await Assert.That(refusal.IsFailed).IsTrue();
        await Assert.That(refusal.Problem?.ErrorCode).IsEqualTo(ErrorCode.RecoveryRequired.ToString());
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(owned, clients.FirstRequest, original.OriginalPhaseCommandId, cancellationToken);
        var repeated = RequirePending(after, original.OriginalPhaseCommandId);
        await SqlRf3Protocol.EqualAsync(retained, repeated);
        var actualHeader = after.First(cut => cut.Pending is not null).Header!;
        await SqlRf3Protocol.EqualAsync(header, actualHeader);
        await Assert.That(actualHeader.PendingOriginalPhaseCommandId).IsEqualTo(original.OriginalPhaseCommandId);
        await Assert.That(actualHeader.RetainedPhaseCount).IsEqualTo(header.RetainedPhaseCount);
        await Assert.That(actualHeader.RetainedMetadataBytes).IsEqualTo(header.RetainedMetadataBytes);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(owned, cancellationToken);
    }

    private async Task RequireSettledAsync(PartitionMoveParentPhase original, Guid cancellationId,
        PartitionMovementPublicParentRf3NativeCut[] interrupted, bool corruptDisposition, CancellationToken cancellationToken)
    {
        var owned = wave!;
        var clients = seed!;
        var actual = interrupted.Where(cut => cut.RetireCancellation is not null).ToArray();
        await Assert.That(actual.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var cut in actual)
        {
            var row = cut.RetireCancellation!;
            await Assert.That(cut.NativeCancellationResult).IsNotNull();
            await Assert.That(cut.NativeCancellationResult!.Error).IsNull();
            await SqlRf3Protocol.EqualAsync(cut.NativeCancellationResult.Get<PartitionMovePhaseResult>().RetireCancellation, row);
            await Assert.That(row.OriginalPhaseCommandId).IsEqualTo(original.OriginalPhaseCommandId);
            await Assert.That(row.CancellationCommandId).IsEqualTo(cancellationId);
            await Assert.That(row.CancellationReceipt.CommandId).IsEqualTo(cancellationId);
            await Assert.That(row.OriginalRequestNonce).IsEqualTo(original.OriginalRequestNonce);
            await Assert.That(row.OriginalExpiresAt).IsEqualTo(original.OriginalExpiresAt);
            await Assert.That(row.CleanupGeneration).IsEqualTo(original.CleanupGeneration);
            await Assert.That(cut.OriginalPhase!.RetireCancellationAttempt!.CancellationCommandId).IsEqualTo(cancellationId);
            await RequireOriginalAsync(original, cut.OriginalPhase!);
        }
        var healthy = corruptDisposition
            ? await PartitionMovementRetireGrantDispositionRf3Trial.RunCorruptAsync(owned, clients, original, cancellationToken)
            : await McpCallerAssertions.SdkSuccessAsync(await clients.Source.MovePartitionAsync(clients.FirstRequest, cancellationToken));
        await Assert.That(healthy.Phase).IsEqualTo(PartitionMovePhase.Retired);
        var cuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(owned, clients.FirstRequest, original.OriginalPhaseCommandId, cancellationToken);
        var header = cuts.First(cut => cut.Header is not null).Header!;
        await Assert.That(header.CleanupGeneration).IsGreaterThan(original.CleanupGeneration);
        await Assert.That(header.PendingOriginalPhaseCommandId).IsNull();
        await SqlRf3Protocol.EqualAsync(header.TerminalResult, healthy);
        await PartitionMovementRetireGrantDispositionRf3Trial.RequireTerminalAsync(cuts, original);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(owned, cancellationToken);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await clients.Source.MovePartitionAsync(
            clients.FirstRequest, cancellationToken));
        await SqlRf3Protocol.EqualAsync(healthy, replay);
        await Assert.That(cancellationId).IsNotEqualTo(original.OriginalPhaseCommandId);
    }

    private static PartitionMoveParentPhase RequirePending(PartitionMovementPublicParentRf3NativeCut[] cuts, Guid originalId)
    {
        var pending = cuts.Where(cut => cut.Pending?.OriginalPhaseCommandId == originalId).Select(cut => cut.Pending!).ToArray();
        if (pending.Length != TwoRf3MembershipProtocol.MembersPerGroup || pending.Any(value => value.Stage != PartitionMovePeerStage.Retire))
        { throw new InvalidOperationException(MissingNativePhase); }
        var bytes = NativeSerialization.Serialize(pending.First());
        if (pending.Any(value => !NativeSerialization.Serialize(value).AsSpan().SequenceEqual(bytes)))
        { throw new InvalidOperationException(MissingNativePhase); }
        return pending.First();
    }

    private static async Task RequireOriginalAsync(PartitionMoveParentPhase original, PartitionMoveParentPhase actual)
    {
        await SqlRf3Protocol.EqualAsync(original.OriginalPhase, actual.OriginalPhase);
        await SqlRf3Protocol.EqualAsync(original.OriginalGrant, actual.OriginalGrant);
        await SqlRf3Protocol.EqualAsync(original.OriginalAuthorization, actual.OriginalAuthorization);
        await SqlRf3Protocol.EqualAsync(original.OriginalReceiverIssuePacket, actual.OriginalReceiverIssuePacket);
        await Assert.That(actual.OriginalRequestNonce).IsEqualTo(original.OriginalRequestNonce);
        await Assert.That(actual.OriginalExpiresAt).IsEqualTo(original.OriginalExpiresAt);
        await Assert.That(actual.OriginalIssuancePolicyEpoch).IsEqualTo(original.OriginalIssuancePolicyEpoch);
        await Assert.That(actual.CleanupGeneration).IsEqualTo(original.CleanupGeneration);
        await Assert.That(actual.OriginalResult).IsNull();
    }

}
