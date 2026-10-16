using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed class ResourcePartitionInventoryRf3Held
{
    private readonly List<Exception> failures = [];
    private Task<ResourcePartitionInventoryRf3Reply>? original;
    private RequestCqrsProbeMarkerRecord? marker;
    private Guid arm;
    private bool originalValidated;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        RemoteTransferNativeMembership membership, bool cancel, Func<CancellationToken, Task>? change,
        CancellationToken token)
    {
        var owner = new ResourcePartitionInventoryRf3Held();
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        await ServerFailureObserver.ObserveAsync(() => owner.ExecuteAsync(
            wave, seed, membership, selected, cancel, change, token), owner.failures).ConfigureAwait(false);
        await owner.CleanupAsync(wave, membership, selected).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(owner.failures);
    }

    private async Task ExecuteAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        RemoteTransferNativeMembership membership, CancellationTokenSource selected, bool cancel,
        Func<CancellationToken, Task>? change, CancellationToken token)
    {
        arm = wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId, Guid.Empty,
            GrainReadKind.ResourceOwnerInventory, RequestCqrsProbePhase.ResourceOwnerInventoryOwnerValidation,
            RequestCqrsProbeAction.Hold, targetVoter: membership.Owners[TwoRf3MembershipProtocol.Node1].VoterId);
        original = ResourcePartitionInventoryRf3Bridge.ReadAsync(wave, seed, membership,
            ResourcePartitionInventoryRf3Protocol.First, selected.Token);
        var held = await ResourcePartitionInventoryRf3HoldBoundary.WaitAsync(
            wave.QueryControls, arm, membership.Source, original, token).ConfigureAwait(false);
        marker = held;
        if (cancel)
        {
            await selected.CancelAsync().ConfigureAwait(false);
            await RequireCancelledAsync(original, selected.Token).ConfigureAwait(false);
        }
        else
        {
            await (change ?? throw new InvalidOperationException("The source policy mutation was not supplied."))(token).ConfigureAwait(false);
            wave.QueryControls.WriteRelease(arm, held.RequestId);
            await ResourcePartitionInventoryRf3Assertions.RefusalAsync(await original.ConfigureAwait(false),
                ErrorCode.TokenInvalidated).ConfigureAwait(false);
        }
        originalValidated = true;
    }

    private async Task CleanupAsync(TwoRf3MembershipWave wave, RemoteTransferNativeMembership membership,
        CancellationTokenSource selected)
    {
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(selected.CancelAsync, failures).ConfigureAwait(false);
        if (marker is { } held)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.ReleaseOpenArmsAsync(
                membership.Source, cleanup.Token), failures).ConfigureAwait(false);
            if (original is { } producer && !originalValidated)
            {
                await ServerFailureObserver.ObserveAsync(async () => _ = await producer.ConfigureAwait(false),
                    failures).ConfigureAwait(false);
            }
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, arm,
                    held.RequestId, Guid.Empty, membership.Source, cleanup.Token).ConfigureAwait(false);
                await wave.QueryControls.RetireArmAsync(arm, cleanup.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        else if (original is { } producer)
        {
            await ServerFailureObserver.ObserveAsync(async () => _ = await producer.ConfigureAwait(false),
                failures).ConfigureAwait(false);
        }
    }

    private static async Task RequireCancelledAsync(Task<ResourcePartitionInventoryRf3Reply> original,
        CancellationToken originalToken)
    {
        var observed = false;
        try
        {
            _ = await original.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (originalToken.IsCancellationRequested) { observed = true; }
        await Assert.That(observed).IsTrue();
        await Assert.That(original.IsCompleted).IsTrue();
    }
}
