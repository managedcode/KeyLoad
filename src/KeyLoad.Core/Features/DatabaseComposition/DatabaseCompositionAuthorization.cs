using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidComposition = "The database composition request is invalid.";
    private const string InvalidLink = "The queue graph link is invalid.";
    private const string MissingReadyMessage = "The ready queue message is unavailable.";
    private const string CompositionLimit = "The database composition source exceeds its budget.";
    private const int MaximumCompositionDepth = 16;
    private const int MaximumCompositionVertices = 10_000;
    private const string FullResourcePath = "";
    private const string WildcardGrant = "*";

    private IReadOnlyList<Mutation> ExpandComposition(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, Mutation mutation, DateTimeOffset now, ReadExecutionBudget budget)
        => mutation switch
        {
            QueueToGraph forward => ExpandQueueToGraph(tx, principal, partition, forward, now, budget),
            GraphToQueueMutation reverse => ExpandGraphToQueue(tx, principal, partition, reverse, budget),
            _ => new Mutation[] { mutation }
        };

    private void AuthorizeComposition(IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        if (mutation is QueueToGraph forward)
        {
            ValidateForward(forward);
            Authorization.Require(principal, partition, forward.Queue, Capability.QueueInspect);
            Authorization.Require(principal, partition, forward.Graph, Capability.GraphWrite);
            var source = Resource(view, partition, forward.Queue, ResourceKind.WorkQueue);
            var target = Resource(view, partition, forward.Graph, ResourceKind.Graph);
            RequireRawSource(principal, source);
            RequireTargetWrites(principal, target);
        }
        else if (mutation is GraphToQueueMutation reverse)
        {
            ValidateReverse(partition, reverse);
            Authorization.Require(principal, partition, reverse.Graph, Capability.GraphRead);
            Authorization.Require(principal, partition, reverse.Queue, Capability.QueuePublish);
            Authorization.Require(principal, partition, reverse.Start.Collection, Capability.DocumentsRead);
            var source = Resource(view, partition, reverse.Graph, ResourceKind.Graph);
            var target = Resource(view, partition, reverse.Queue, ResourceKind.WorkQueue);
            Resource(view, partition, reverse.Start.Collection, ResourceKind.Collection);
            RequireRawSource(principal, source);
            RequireTargetWrites(principal, target);
        }
    }

    private void RequireRawSource(PrincipalRecord principal, ResourceDefinition source)
    {
        Authorization.RequireFieldUse(principal, source, FullResourcePath);
        foreach (var policy in source.FieldPolicies)
        {
            if (!HasGrant(principal, policy.RawReadGrant))
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, "A source field requires raw-read authority.");
            }
        }
    }

    private static bool HasGrant(PrincipalRecord principal, string grant)
        => principal.ClusterAdministrator || principal.FieldGrants.Contains(WildcardGrant, StringComparer.Ordinal)
            || principal.FieldGrants.Contains(grant, StringComparer.Ordinal);

    private void RequireTargetWrites(PrincipalRecord principal, ResourceDefinition target)
    {
        foreach (var policy in target.FieldPolicies)
        {
            Authorization.RequireFieldWrite(principal, target, policy.Path);
        }
        foreach (var policy in target.HeaderPolicies)
        {
            Authorization.RequireFieldWrite(principal, target with { FieldPolicies = target.HeaderPolicies }, policy.Path);
        }
    }

    private void ValidateForward(QueueToGraph command)
    {
        JsonData.Identifier(command.Graph);
        JsonData.Identifier(command.Queue);
        JsonData.Identifier(command.EdgeIdPrefix);
        if (command.Resource != command.Graph || command.MaxMessages < 1
            || command.MaxMessages > Limits.MaxBatchMutations)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidComposition);
        }
    }

    private void ValidateReverse(PartitionRef partition, GraphToQueueMutation command)
    {
        JsonData.Identifier(command.Queue);
        JsonData.Identifier(command.Graph);
        JsonData.Identifier(command.MessageIdPrefix);
        if (command.Resource != command.Queue || command.Start is null || command.Start.Partition != partition
            || command.MaxDepth is < 1 or > MaximumCompositionDepth
            || command.MaxVertices is < 1 or > MaximumCompositionVertices
            || command.MaxEdges < 1 || command.MaxEdges > Limits.MaxBatchMutations)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidComposition);
        }
        JsonData.Identifier(command.Start.Collection);
        JsonData.Identifier(command.Start.Id);
    }

}
