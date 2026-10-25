using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DatabaseCompositionExecution operations against the caller's database context.</summary>
public static class DatabaseCompositionExecution
{
    internal const string DatabaseCompositionAuthorizationSourceFieldRequiresRawReadAuthorityDetail = "A source field requires raw-read authority.";
    internal const int DatabaseCompositionAuthorizationMinimumPositiveCount = 1;
    internal const string InvalidComposition = "The database composition request is invalid.";
    internal const string InvalidLink = "The queue graph link is invalid.";
    internal const string MissingReadyMessage = "The ready queue message is unavailable.";
    internal const string CompositionLimit = "The database composition source exceeds its budget.";
    internal const string FullResourcePath = "";
    internal const string WildcardGrant = "*";
    internal static IReadOnlyList<Mutation> ExpandComposition(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, Mutation mutation, DateTimeOffset now, ReadExecutionBudget budget) => mutation switch
    {
        QueueToGraph forward => engine.ExpandQueueToGraph(tx, principal, partition, forward, now, budget),
        GraphToQueueMutation reverse => engine.ExpandGraphToQueue(tx, principal, partition, reverse, budget),
        _ => new Mutation[]
        {
            mutation
        }
    };
    internal static (string FromPrimaryKey, string ToPrimaryKey)? AuthorizeComposition(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        if (mutation is QueueToGraph forward)
        {
            DatabaseCompositionExecution.ValidateForward(forward);
            engine.RequireCompositionCapabilities(principal, partition, forward);
            var source = engine.BatchResource(view, partition, forward.Queue, ResourceKind.WorkQueue);
            var target = engine.BatchResource(view, partition, forward.Graph, ResourceKind.Graph);
            engine.RequireRawSource(principal, source);
            engine.RequireTargetWrites(principal, target);
            return engine.AuthorizeTypedEndpointJoin(view, principal, partition, forward);
        }
        else if (mutation is GraphToQueueMutation reverse)
        {
            engine.ValidateReverse(partition, reverse);
            engine.RequireCompositionCapabilities(principal, partition, reverse);
            var source = engine.BatchResource(view, partition, reverse.Graph, ResourceKind.Graph);
            var target = engine.BatchResource(view, partition, reverse.Queue, ResourceKind.WorkQueue);
            engine.BatchResource(view, partition, reverse.Start.Collection, ResourceKind.Collection);
            engine.RequireRawSource(principal, source);
            engine.RequireTargetWrites(principal, target);
        }

        return null;
    }

    internal static void RequireCompositionCapabilities(this DatabaseEngine engine, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        if (mutation is QueueToGraph forward)
        {
            engine.Authorization.Require(principal, partition, forward.Queue, Capability.QueueInspect);
            engine.Authorization.Require(principal, partition, forward.Graph, Capability.GraphWrite);
        }
        else if (mutation is GraphToQueueMutation reverse)
        {
            engine.Authorization.Require(principal, partition, reverse.Graph, Capability.GraphRead);
            engine.Authorization.Require(principal, partition, reverse.Queue, Capability.QueuePublish);
            engine.Authorization.Require(principal, partition, reverse.Start.Collection, Capability.DocumentsRead);
        }
    }

    internal static void RequireRawSource(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition source)
    {
        engine.Authorization.RequireFieldUse(principal, source, FullResourcePath);
        foreach (var policy in source.FieldPolicies)
        {
            if (!HasGrant(principal, policy.RawReadGrant))
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, DatabaseCompositionAuthorizationSourceFieldRequiresRawReadAuthorityDetail);
            }
        }
    }

    internal static bool HasGrant(PrincipalRecord principal, string grant) => principal.ClusterAdministrator || principal.FieldGrants.Contains(WildcardGrant, StringComparer.Ordinal) || principal.FieldGrants.Contains(grant, StringComparer.Ordinal);
    internal static void RequireTargetWrites(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition target)
    {
        foreach (var policy in target.FieldPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, target, policy.Path);
        }

        foreach (var policy in target.HeaderPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, target with { FieldPolicies = target.HeaderPolicies }, policy.Path);
        }
    }

    internal static void ValidateForward(QueueToGraph command)
    {
        JsonData.Identifier(command.Graph);
        JsonData.Identifier(command.Queue);
        JsonData.Identifier(command.EdgeIdPrefix);
        if (command.Resource != command.Graph || command.MaxMessages < DatabaseCompositionAuthorizationMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidComposition);
        }
    }

    internal static void ValidateReverse(this DatabaseEngine engine, PartitionRef partition, GraphToQueueMutation command)
    {
        JsonData.Identifier(command.Queue);
        JsonData.Identifier(command.Graph);
        JsonData.Identifier(command.MessageIdPrefix);
        if (command.Resource != command.Queue || command.Start is null || command.Start.Partition != partition || command.MaxDepth < DatabaseCompositionAuthorizationMinimumPositiveCount || command.MaxDepth > engine.GraphExecution.MaximumDepth || command.MaxVertices < DatabaseCompositionAuthorizationMinimumPositiveCount || command.MaxVertices > engine.GraphExecution.MaximumVertices || command.MaxEdges < DatabaseCompositionAuthorizationMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidComposition);
        }

        JsonData.Identifier(command.Start.Collection);
        JsonData.Identifier(command.Start.Id);
    }
}
