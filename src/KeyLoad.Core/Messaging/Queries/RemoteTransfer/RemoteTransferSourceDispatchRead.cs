using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteTransferSourceDispatch? CaptureRemoteTransferDispatch(string principalId,
        ReplicatedOperation operation, ReadExecutionBudget work)
        => CaptureRemoteTransferDispatch(principalId, Payload<CommandRequest>(operation), work);

    internal RemoteTransferSourceDispatch? CaptureRemoteTransferDispatch(string principalId,
        CommandRequest request, ReadExecutionBudget work)
    {
        work.Check();
        if (request.Mutations.Length != RemoteTransferPeerProtocol.SingleMutation
            || request.Mutations[RemoteTransferPeerProtocol.FirstMutation] is not AcceptQueueTransfer accept)
        {
            return null;
        }
        work.ChargeBytes(NativeSerialization.Measure(request));
        return Store.Read(view => ReadRemoteTransferDispatch(work.CreateView(view), principalId, request, accept, work));
    }

    private RemoteTransferSourceDispatch? ReadRemoteTransferDispatch(IKeyValueView view, string principalId,
        CommandRequest request, AcceptQueueTransfer accept, ReadExecutionBudget work)
    {
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        ValidateTransferDestination(accept.DestinationQueue, request.Partition);
        RequireTransferAdministrator(principal);
        if (accept.IntentToken is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
        var claims = Verify<RemoteTransferIntentClaims>(accept.IntentToken, Limits.MaxBatchBytes);
        ValidateIntentClaims(claims, accept.DestinationQueue, principal.Id);
        var current = CaptureRemoteTransferTarget(view, claims.Destination);
        if (current is null)
        {
            return null;
        }
        RequireLocalResourceOwner(view, claims.Source.Partition);
        var intent = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(claims.Source, claims.TransferId))
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferPeerProtocol.Unavailable);
        ValidateIntentRecord(view, intent, claims.Source, claims.TransferId);
        if (intent.IntentToken != accept.IntentToken || intent.PrincipalId != principal.Id
            || intent.RemoteTarget is null
            || !PhysicalOwnerEntryValidation.Same(intent.RemoteTarget.DestinationOwner, current.DestinationOwner))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid);
        }
        ValidateRemoteTransferOriginalSourceCommit(view, intent);
        RequireTransferCapabilities(principal, claims.Source, Capability.QueuePublish);
        RequireTransferCapabilities(principal, claims.Destination, Capability.QueuePublish);
        RequireTransferCapabilities(principal, claims.Destination, Capability.QueueInspect);
        var source = Resource(view, claims.Source.Partition, claims.Source.Queue, ResourceKind.WorkQueue);
        var destination = RemoteTransferDestinationResource(view, claims.Destination);
        RequireTransferPublisher(principal, claims.Source, source);
        RequireTransferPublisher(principal, claims.Destination, destination);
        RequireTransferInspector(principal, claims.Destination, destination);
        if (view is IResourceLifetimeReadBindingView)
        {
            _ = BindAuthorizedReadResource(view, claims.Source.Partition, claims.Source.Queue, ResourceKind.WorkQueue);
            _ = RemoteTransferDestinationReadResource(view, claims.Destination);
        }
        _ = RemoteTransferStorage.RequireSourceCounter(view, claims.Source);
        return CaptureRemoteTransferSourcePolicy(view, principal, claims, intent, source, destination, work);
    }

    private RemoteTransferSourceDispatch CaptureRemoteTransferSourcePolicy(IKeyValueView view,
        PrincipalRecord principal, RemoteTransferIntentClaims claims, RemoteTransferIntentRecord intent,
        ResourceDefinition source, ResourceDefinition destination, ReadExecutionBudget work)
    {
        var lifetime = ResourceLifetimeCatalog.Read(view, claims.Source.Partition.TenantId,
            claims.Source.Partition.DatabaseId, claims.Source.Queue, source)
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
        var encodedPolicy = NativeSerialization.Serialize(destination);
        work.ChargeBytes(encodedPolicy.Length);
        var digest = Convert.ToHexString(SHA256.HashData(encodedPolicy));
        var target = intent.RemoteTarget ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
        var result = new RemoteTransferSourceDispatch(claims, intent, target, principal.PolicyEpoch,
            digest, Token(view, claims.Source.Partition, Store.Position), source, lifetime);
        work.Check();
        return result;
    }
}
