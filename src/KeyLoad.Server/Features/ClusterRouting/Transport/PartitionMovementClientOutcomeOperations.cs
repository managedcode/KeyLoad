using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementClientOutcomeOperations
{
    internal static async Task<PartitionMovementOutcomeWitness> QueryOutcomeAsync(PartitionMovementClient owner, Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization, string originalVoter,
        DateTimeOffset requestExpiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        cancellationToken.ThrowIfCancellationRequested();
        if (owner.options.Value.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority
            || !owner.options.Value.MembershipAuthority.RegisterPhysicalOwners || requestExpiry <= owner.clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        var remaining = requestExpiry - owner.clock.GetUtcNow();
        var bounded = remaining < work.RemainingLifetime ? remaining : work.RemainingLifetime;
        if (bounded <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        using var deadline = new CancellationTokenSource(bounded, owner.clock);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var originalToken = caller.Token;
        await owner.partition.Coordinator.ReadBarrierAsync(originalToken).ConfigureAwait(false);
        work.Check();
        owner.partition.Database.ValidatePartitionMovementOutcomeQuery(original, authorization, phaseCommandId, work);
        work.Check();
        var receiver = original.Grant?.ReceiverOwner ?? original.ControlOwner;
        var control = PhysicalOwnerConfiguredTuples.Control(owner.options.Value, owner.partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(owner.options.Value, owner.partition);
        var local = PhysicalOwnerEntryValidation.SameOwner(receiver, control.Owner);
        var selected = local ? control : destination;
        var index = selected.Owner.VoterIds.IndexOf(originalVoter);
        if (index < PartitionMovementClient.FirstVoter || !PhysicalOwnerEntryValidation.SameOwner(receiver, selected.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        var failures = new List<Exception>();
        for (var attempt = PartitionMovementClient.FirstVoter; attempt < selected.Owner.VoterIds.Length; attempt++)
        {
            try
            {
                work.Check();
                originalToken.ThrowIfCancellationRequested();
                var voter = (index + attempt) % selected.Owner.VoterIds.Length;
                return await PartitionMovementClientOutcomeVoterOperations.QueryOutcomeVoterAsync(owner, phaseCommandId, original, authorization, selected, voter,
                    selected.Owner.VoterIds.Length - attempt, local, requestExpiry, work,
                    originalToken).ConfigureAwait(false);
            }
            catch (PartitionMovementOutcomeNetworkException failure)
            { failures.Add(failure.InnerException ?? failure); }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
    }
}
