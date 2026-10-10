using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal OperationResult? ReadOnlineTextOriginalOutcome(string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
        => Store.Read(raw =>
        {
            var view = budget.CreateView(raw);
            var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
            return ReadOnlineTextOriginalOutcome(view, principal, request, budget);
        });

    internal OperationResult? ReadOnlineTextOriginalOutcome(IKeyValueView view, PrincipalRecord principal,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        RequireOnlineTextOriginalAuthority(view, principal, request);
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.Consumer.Partition);
        var previous = CommandOutcomeKeyResolver.Select(view, principal.Id, request.CommandId, scope).Outcome;
        if (previous is null)
        { return null; }
        ValidateOnlineTextStoredOutcome(view, principal, request, previous, budget);
        budget.Check();
        return previous.Result;
    }

    internal OnlineTextCurrentPublication? ReadOnlineTextOriginalPublication(string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
        => Store.Read(raw =>
        {
            var view = budget.CreateView(raw);
            var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
            RequireOnlineTextOriginalAuthority(view, principal, request);
            var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.Consumer.Partition);
            var previous = CommandOutcomeKeyResolver.Select(view, principal.Id, request.CommandId, scope).Outcome;
            if (previous is null)
            { return null; }
            ValidateOnlineTextStoredOutcome(view, principal, request, previous, budget);
            var result = previous.Result.Get<OnlineTextIndexMaintenanceResult>();
            var authority = previous.OnlineTextAuthority ?? throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.MissingAuthority);
            return new OnlineTextCurrentPublication(OnlineTextPublicationProtocol.CurrentFormat, principal.Id,
                request.CommandId, authority.ParentFingerprint, request.Consumer, request.ConsumerGeneration,
                authority, result.PublishedCut);
        });

    private void ValidateOnlineTextCachedOutcome(IKeyValueView view, PrincipalRecord principal,
        ReplicatedOperation operation, StoredOutcome previous)
    {
        if (operation.Kind != OperationKind.OnlineTextPublicationPhase)
        { return; }
        var command = Payload<OnlineTextPublicationPhaseCommand>(operation);
        RequireOnlineTextOriginalAuthority(view, principal, command.Request);
        ValidateOnlineTextStoredOutcome(view, principal, command.Request, previous, null);
    }

    private void ValidateOnlineTextStoredOutcome(IKeyValueView view, PrincipalRecord principal,
        OnlineTextIndexMaintenanceRequest request, StoredOutcome previous, ReadExecutionBudget? budget)
    {
        if (previous.OnlineTextAuthority is not { } authority
            || authority.ParentFingerprint != OnlineTextParentIdentity.Fingerprint(principal.Id, request)
            || authority.Collection != request.Collection || authority.Field != request.Field)
        { throw Errors.Fail(ErrorCode.Conflict, OnlineTextPublicationProtocol.ChangedOriginal); }
        if (previous.Incarnation != Store.Identity.Incarnation || authority.DataEpoch != Store.Identity.FormatVersion)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, OnlineTextPublicationProtocol.MissingAuthority); }
        if (previous.PolicyEpoch != principal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, OnlineTextPublicationProtocol.MissingAuthority); }
        if (previous.Result.Error is not null)
        { return; }
        var original = previous.Result.Get<OnlineTextIndexMaintenanceResult>();
        var resource = Resource(view, request.Consumer.Partition, request.Collection, ResourceKind.Collection);
        budget?.ChargeBytes(NativeSerialization.Measure(resource));
        var sha = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(resource)));
        if (original.CommandId != request.CommandId || original.Consumer != request.Consumer
            || original.ConsumerGeneration != request.ConsumerGeneration
            || original.PublishedCut.ResourceSha256 != sha
            || original.PublishedCut.SchemaVersion != resource.SchemaVersion)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, OnlineTextPublicationProtocol.MissingAuthority); }
        budget?.CheckResult(original);
    }

    private void RequireOnlineTextOriginalAuthority(IKeyValueView view, PrincipalRecord principal,
        OnlineTextIndexMaintenanceRequest request)
    {
        _ = RequireActiveProjectionConsumer(view, principal, request.Consumer, request.ConsumerGeneration);
        Authorization.Require(principal, request.Consumer.Partition, request.Collection,
            Capability.Query | Capability.DocumentsRead);
        var resource = Resource(view, request.Consumer.Partition, request.Collection, ResourceKind.Collection);
        Authorization.RequireFieldUse(principal, resource, request.Field);
        var current = ReadAtomicPartitionPlacement(view, principal.Id,
            new(AtomicPartitionPlacementProtocol.CurrentVersion, request.Consumer.Partition));
        if (current.PhysicalShardId != request.Placement.PhysicalShardId
            || current.Incarnation != request.Placement.Incarnation
            || current.PlacementEpoch != request.Placement.PlacementEpoch
            || !current.VoterIds.SequenceEqual(request.Placement.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, OnlineTextPublicationProtocol.MissingAuthority); }
    }
}
