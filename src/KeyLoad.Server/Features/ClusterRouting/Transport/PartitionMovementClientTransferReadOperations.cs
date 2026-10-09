using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementClientTransferReadOperations
{
    internal static async Task<GrainOperationReply> ReadTransferDataAsync(PartitionMovementClient owner, string principalId,
        PartitionMovementAuthenticatedAuthority originalAuthority, PartitionMovementTransferDataAction action,
        Guid handleId, int ordinal, DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        cancellationToken.ThrowIfCancellationRequested();
        if (owner.options.Value.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority
            || !owner.options.Value.MembershipAuthority.RegisterPhysicalOwners || expiry <= owner.clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        var authorityReply = NativeSerialization.Deserialize<PartitionMovementTransferAuthorityReply>(originalAuthority.ReplyBytes);
        if (authorityReply.Reply.Error is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        var authority = GrainNativePayload.Read<GrainValue>(authorityReply.Reply.Payload).Value as PartitionMovementTransferAuthorityResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        PartitionMoveTransferAuthorityValidation.Require(authority.Authority, owner.partition.Database.Limits);
        if (authority.Authority.Header.OperatorPrincipalId != principalId)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var control = PhysicalOwnerConfiguredTuples.Control(owner.options.Value, owner.partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(owner.options.Value, owner.partition);
        var local = PhysicalOwnerEntryValidation.SameOwner(authorityReply.SourceOwner, control.Owner);
        var selected = local ? control : destination;
        if (!PhysicalOwnerEntryValidation.SameOwner(authorityReply.SourceOwner, selected.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.InvalidProof); }
        var discovery = owner.node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var capability = new PartitionMovementTransferDataCapability(action, originalAuthority.ReplyBytes,
            originalAuthority.Signature, handleId, ordinal, grant.RemainingBytes, grant.RemainingRecords,
            work.MaximumResultBytes);
        var request = new PartitionMovementTransferDataRequest(Guid.NewGuid(), Guid.NewGuid(), expiry,
            principalId, owner.partition.Configuration.LocalId, discovery.SiloAddress, capability);
        if (NativeSerialization.Measure(request) > owner.partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var reply = await owner.transport.ExchangeTransferDataAsync(PartitionMovementClient.FirstVoter, selected, request,
            NativeSerialization.Serialize(request), local, cancellationToken).ConfigureAwait(false);
        work.Check();
        if (reply.Reply.Error is { } code)
        { throw Errors.Fail(code, reply.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var actual = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementTransferDataResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        if (actual.Action != action || (actual.Handle is not null) != (action == PartitionMovementTransferDataAction.Open)
            || (actual.Page is not null) != (action == PartitionMovementTransferDataAction.Page)
            || (actual.Closed is not null) != (action == PartitionMovementTransferDataAction.Close))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof); }
        work.MeasureResult(reply);
        work.ImportReadGrant(grant, actual.ReadBytes, actual.ExaminedRecords);
        work.CompleteReadGrant(grant);
        return reply.Reply;
    }
}
