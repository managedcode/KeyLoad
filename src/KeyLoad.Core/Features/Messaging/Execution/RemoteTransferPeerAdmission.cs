using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteTransferNativeProof AdmitRemoteTransferPeerCall(ReadOnlyMemory<byte> originalEnvelope,
        string originalSignature, RemoteQueueTransferPeerCall call, ReadExecutionBudget work)
    {
        work.Check();
        RequireNativeBudget(originalEnvelope.Length);
        work.ChargeBytes(originalEnvelope.Length);
        RemoteTransferPeerShape.Require(call);
        var evaluatedAt = Clock.GetUtcNow();
        if (call.ExpiresAt <= evaluatedAt || call.MaximumReplyBytes > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
        var owner = RequireRemoteTransferPeerOwner();
        var proof = Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, owner.TechnicalPrincipal, evaluatedAt);
            var value = new RemoteTransferNativeProof(originalEnvelope.ToArray(), originalSignature, call,
                principal.Id, principal.PolicyEpoch, evaluatedAt);
            owner.Require(value);
            RequireRemoteTransferPeerAtView(charged, principal, value);
            return value;
        });
        RequireNativeBudget(NativeSerialization.Measure(proof));
        work.Check();
        return proof;
    }

    private void RequireRemoteTransferPeerAtView(IKeyValueView view, PrincipalRecord principal,
        RemoteTransferNativeProof proof)
    {
        var call = proof.Call;
        RemoteTransferPeerShape.Require(call);
        RequireTransferAdministrator(principal);
        if (proof.TechnicalPrincipalId != principal.Id || proof.TechnicalPolicyEpoch != principal.PolicyEpoch
            || configuredPhysicalOwner is null || !PhysicalOwnerEntryValidation.SameOwner(configuredPhysicalOwner, call.DestinationOwner.Owner)
            || Store.Identity.Incarnation != call.DestinationOwner.Owner.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid); }
        var destination = call.IntentClaims.Destination;
        ValidateTransferPair(call.IntentClaims.Source, destination);
        var placement = ReadPlacementWitness(view, destination.Partition);
        if (placement.PhysicalShardId != call.DestinationOwner.Owner.PhysicalShardId
            || placement.Incarnation != call.DestinationOwner.Owner.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid); }
        var resource = Resource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
        if (call.Stage == RemoteQueueTransferPeerStage.Accept)
        { RequireTransferPublisher(principal, destination, resource); }
        else
        { RequireTransferInspector(principal, destination, resource); }
    }
}
