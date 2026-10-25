using System.Security.Cryptography;
using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.Search;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes OnlineTextOriginalOutcome operations against the caller's database context.</summary>
public static class OnlineTextOriginalOutcome
{
    internal static Result<object>? ReadOnlineTextOriginalOutcome(this DatabaseEngine engine, string principalId, OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget) => engine.Store.Read(raw =>
    {
        var view = budget.CreateView(raw);
        var principal = engine.Principal(view, principalId, engine.EvaluationClock.GetUtcNow());
        return (engine.ReadOnlineTextOriginalOutcome(view, principal, request, budget)).Value;
    });
    internal static (bool HasValue, Result<object>? Value) ReadOnlineTextOriginalOutcome(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        engine.RequireOnlineTextOriginalAuthority(view, principal, request);
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.Consumer.Partition);
        var previous = CommandOutcomeKeyResolver.Select(view, principal.Id, request.CommandId, scope).Outcome;
        if (previous is null)
        {
            return (false, default(Result<object>?));
        }

        engine.ValidateOnlineTextStoredOutcome(view, principal, request, previous, budget);
        budget.Check();
        return (true, ((Result<object>?)(previous.Result)));
    }

    internal static OnlineTextCurrentPublication? ReadOnlineTextOriginalPublication(this DatabaseEngine engine, string principalId, OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget) => engine.Store.Read(raw =>
    {
        var view = budget.CreateView(raw);
        var principal = engine.Principal(view, principalId, engine.EvaluationClock.GetUtcNow());
        engine.RequireOnlineTextOriginalAuthority(view, principal, request);
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.Consumer.Partition);
        var previous = CommandOutcomeKeyResolver.Select(view, principal.Id, request.CommandId, scope).Outcome;
        if (previous is null)
        {
            return null;
        }

        engine.ValidateOnlineTextStoredOutcome(view, principal, request, previous, budget);
        var result = previous.Result.Get<OnlineTextIndexMaintenanceResult>();
        var authority = previous.OnlineTextAuthority ?? throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.MissingAuthority);
        return new OnlineTextCurrentPublication(OnlineTextPublicationProtocol.CurrentFormat, principal.Id, request.CommandId, authority.ParentFingerprint, request.Consumer, request.ConsumerGeneration, authority, result.PublishedCut);
    });
    internal static void ValidateOnlineTextCachedOutcome(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, StoredOutcome previous)
    {
        if (operation.Value!.Kind != OperationKind.OnlineTextPublicationPhase)
        {
            return;
        }

        var command = OperationProtocol.Payload<OnlineTextPublicationPhaseCommand>(operation);
        engine.RequireOnlineTextOriginalAuthority(view, principal, command.Request);
        engine.ValidateOnlineTextStoredOutcome(view, principal, command.Request, previous, null);
    }

    internal static void ValidateOnlineTextStoredOutcome(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, OnlineTextIndexMaintenanceRequest request, StoredOutcome previous, ReadExecutionBudget? budget)
    {
        if (previous.OnlineTextAuthority is not { } authority || authority.ParentFingerprint != OnlineTextParentIdentity.Fingerprint(principal.Id, request) || authority.Collection != request.Collection || authority.Field != request.Field)
        {
            throw Errors.Fail(ErrorCode.Conflict, OnlineTextPublicationProtocol.ChangedOriginal);
        }

        if (previous.Incarnation != engine.Store.Identity.Incarnation || authority.DataEpoch != engine.Store.Identity.FormatVersion)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, OnlineTextPublicationProtocol.MissingAuthority);
        }

        if (previous.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, OnlineTextPublicationProtocol.MissingAuthority);
        }

        if (Errors.Code(previous.Result.Problem).Value is not null)
        {
            return;
        }

        var original = previous.Result.Get<OnlineTextIndexMaintenanceResult>();
        var resource = engine.Resource(view, request.Consumer.Partition, request.Collection, ResourceKind.Collection);
        budget?.ChargeBytes(OrleansSerialization.Measure(resource));
        var sha = Convert.ToHexStringLower(SHA256.HashData(OrleansSerialization.Serialize(resource)));
        if (original.CommandId != request.CommandId || original.Consumer != request.Consumer || original.ConsumerGeneration != request.ConsumerGeneration || original.PublishedCut.ResourceSha256 != sha || original.PublishedCut.SchemaVersion != resource.SchemaVersion)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, OnlineTextPublicationProtocol.MissingAuthority);
        }

        budget?.CheckResult(original);
    }

    internal static void RequireOnlineTextOriginalAuthority(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, OnlineTextIndexMaintenanceRequest request)
    {
        _ = ProjectionConsumerAuthority.RequireActiveProjectionConsumer(view, principal, request.Consumer, request.ConsumerGeneration);
        engine.Authorization.Require(principal, request.Consumer.Partition, request.Collection, Capability.Query | Capability.DocumentsRead);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, request.Consumer.Partition, request.Collection, ResourceKind.Collection);
        engine.Authorization.RequireFieldUse(principal, resource, request.Field);
        var current = engine.ReadAtomicPartitionPlacement(view, principal.Id, request.Consumer.Partition);
        if (current.PhysicalShardId != request.Placement.PhysicalShardId || current.Incarnation != request.Placement.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OnlineTextPublicationProtocol.MissingAuthority);
        }
    }
}
