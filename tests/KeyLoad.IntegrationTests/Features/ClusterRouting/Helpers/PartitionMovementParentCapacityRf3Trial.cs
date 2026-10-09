using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Separates observed-shape boundary controls from actual configured grant refusal and honest native outcome deltas.</summary>
internal sealed class PartitionMovementParentCapacityRf3Trial
{
    private const string ClusterPrefix = "keyload-";
    private const int HexCharactersPerByte = 2;
    private const int OneByte = 1;
    private TwoRf3MembershipWave? wave;
    private PartitionMovementPublicParentRf3Seed? seed;

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var trial = new PartitionMovementParentCapacityRf3Trial();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => trial.ExecuteAsync(caller.Token), failures).ConfigureAwait(false);
        if (trial.seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (trial.wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var legal = new DatabaseLimits().MaxBatchBytes;
        wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(legal, cancellationToken).ConfigureAwait(false);
        seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, cancellationToken).ConfigureAwait(false);
        _ = await PartitionMovementParentCapacityRf3Producer.HoldAndJoinAsync(wave, seed, cancellationToken).ConfigureAwait(false);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        var control = before.First(cut => cut.Header is not null);
        var state = PartitionMovementParentCapacityRf3Snapshot.Read(wave, control.Node, seed, seed.FirstRequest,
            legal, cancellationToken);
        var structuralBoundary = await PartitionMovementParentCapacityRf3Snapshot.RequireObservedBoundaryAsync(state,
            ClusterPrefix + wave.Profile.Incarnation.ToString("N"), cancellationToken).ConfigureAwait(false);
        var after = TwoRf3MembershipProtocol.Nodes.Select(node => PartitionMovementPublicParentRf3Cut.ReadStopped(
            wave, node, seed.FirstRequest, Guid.Empty)).ToArray();
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await AbortAsync(seed.FirstRequest, cancellationToken).ConfigureAwait(false);
        var baseline = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        // This measured raw-row ceiling keeps the existing retained rows readable. It is not the product +1 threshold.
        var deniedLimit = checked(baseline.SelectMany(cut => cut.Rows)
            .Max(row => (row.Length - row.IndexOf(':', StringComparison.Ordinal) - OneByte) / HexCharactersPerByte) + OneByte);
        await Assert.That((long)deniedLimit).IsLessThan(structuralBoundary);
        await wave.ReconfigureMovementCapacityAsync(deniedLimit, cancellationToken).ConfigureAwait(false);
        var rejected = seed.FirstRequest with { MoveId = Guid.NewGuid() };
        var actual = await seed.Source.MovePartitionAsync(rejected, cancellationToken).ConfigureAwait(false);
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
        var failed = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, rejected,
            cancellationToken).ConfigureAwait(false);
        await RequireGrantDeniedAsync(rejected, failed, deniedLimit).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(baseline.Skip(TwoRf3MembershipProtocol.MembersPerGroup).Select(cut => cut.Rows).ToArray(),
            failed.Skip(TwoRf3MembershipProtocol.MembersPerGroup).Select(cut => cut.Rows).ToArray());
        await wave.ReconfigureMovementCapacityAsync(legal, cancellationToken).ConfigureAwait(false);
        await AbortAsync(rejected, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        await HealthyAsync(seed.FirstRequest with { MoveId = Guid.NewGuid() }, cancellationToken).ConfigureAwait(false);
    }

    private async Task AbortAsync(PartitionMoveRequest request, CancellationToken cancellationToken)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await seed!.Source.MovePartitionAsync(
            request with { Mode = PartitionMoveMode.Abort }, cancellationToken).ConfigureAwait(false));
        await Assert.That(result.MoveId).IsEqualTo(request.MoveId);
        await Assert.That(result.Partition).IsEqualTo(request.Partition);
        await Assert.That(result.Phase).IsEqualTo(PartitionMovePhase.Aborted);
    }

    private async Task HealthyAsync(PartitionMoveRequest request, CancellationToken cancellationToken)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await seed!.Source.MovePartitionAsync(request,
            cancellationToken).ConfigureAwait(false));
        await Assert.That(result.MoveId).IsEqualTo(request.MoveId);
        await Assert.That(result.Partition).IsEqualTo(request.Partition);
        await Assert.That(result.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(seed, request, seed.OriginalPlacement,
            seed.Directory.ControlOwner, result, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave!, request,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave!, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, request, result,
            cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave!, request,
            cancellationToken).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave!, cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireGrantDeniedAsync(PartitionMoveRequest request,
        PartitionMovementPublicParentRf3NativeCut[] cuts, int actualLimit)
    {
        var control = cuts.Where(cut => cut.Header is not null).ToArray();
        await Assert.That(control.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var cut in control)
        {
            await Assert.That(cut.Pending).IsNull();
            await Assert.That(cut.LastIssued!.Stage).IsEqualTo(PartitionMovePeerStage.ControlAuthorize);
            await Assert.That(cut.LastIssued.OriginalResult!.Error).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(cut.LastIssued.ObservationCheckpointReceipt).IsNotNull();
            await Assert.That(cut.LastIssued.OriginalGrant).IsNull();
            await Assert.That(cut.OutstandingMoveGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
            var body = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(cut.LastIssued.OriginalPhase!.Body.Span);
            await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
            await Assert.That(cut.OutstandingPrincipalGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
            var grant = PartitionMovementParentCapacityRf3Snapshot.ReadGrant(wave!, cut.Node, request, body.GrantId, actualLimit);
            await Assert.That(grant).IsNull();
        }
    }
}
