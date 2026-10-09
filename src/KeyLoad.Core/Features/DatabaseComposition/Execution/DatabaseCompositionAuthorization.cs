using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string DatabaseCompositionAuthorizationSourceFieldRequiresRawReadAuthorityDetail = "A source field requires raw-read authority.";
    private const int DatabaseCompositionAuthorizationMinimumPositiveCount = 1;

    private const string InvalidComposition = "The database composition request is invalid.";
    private const string InvalidLink = "The queue graph link is invalid.";
    private const string MissingReadyMessage = "The ready queue message is unavailable.";
    private const string CompositionLimit = "The database composition source exceeds its budget.";
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

    private void AuthorizeComposition(IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation,
        BatchResourceAdmission admission = BatchResourceAdmission.CurrentPhysicalOwner)
    {
        if (mutation is QueueToGraph forward)
        {
            ValidateForward(forward);
            RequireCompositionCapabilities(principal, partition, forward);
            var source = BatchResource(view, partition, forward.Queue, admission, ResourceKind.WorkQueue);
            var target = BatchResource(view, partition, forward.Graph, admission, ResourceKind.Graph);
            RequireRawSource(principal, source);
            RequireTargetWrites(principal, target);
        }
        else if (mutation is GraphToQueueMutation reverse)
        {
            ValidateReverse(partition, reverse);
            RequireCompositionCapabilities(principal, partition, reverse);
            var source = BatchResource(view, partition, reverse.Graph, admission, ResourceKind.Graph);
            var target = BatchResource(view, partition, reverse.Queue, admission, ResourceKind.WorkQueue);
            BatchResource(view, partition, reverse.Start.Collection, admission, ResourceKind.Collection);
            RequireRawSource(principal, source);
            RequireTargetWrites(principal, target);
        }
    }

    private void RequireCompositionCapabilities(PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        if (mutation is QueueToGraph forward)
        {
            Authorization.Require(principal, partition, forward.Queue, Capability.QueueInspect);
            Authorization.Require(principal, partition, forward.Graph, Capability.GraphWrite);
        }
        else if (mutation is GraphToQueueMutation reverse)
        {
            Authorization.Require(principal, partition, reverse.Graph, Capability.GraphRead);
            Authorization.Require(principal, partition, reverse.Queue, Capability.QueuePublish);
            Authorization.Require(principal, partition, reverse.Start.Collection, Capability.DocumentsRead);
        }
    }

    private void RequireRawSource(PrincipalRecord principal, ResourceDefinition source)
    {
        Authorization.RequireFieldUse(principal, source, FullResourcePath);
        foreach (var policy in source.FieldPolicies)
        {
            if (!HasGrant(principal, policy.RawReadGrant))
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, DatabaseCompositionAuthorizationSourceFieldRequiresRawReadAuthorityDetail);
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
        if (command.Resource != command.Graph || command.MaxMessages < DatabaseCompositionAuthorizationMinimumPositiveCount
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
            || command.MaxDepth < DatabaseCompositionAuthorizationMinimumPositiveCount || command.MaxDepth > graphExecution.MaximumDepth
            || command.MaxVertices < DatabaseCompositionAuthorizationMinimumPositiveCount || command.MaxVertices > graphExecution.MaximumVertices
            || command.MaxEdges < DatabaseCompositionAuthorizationMinimumPositiveCount || command.MaxEdges > Limits.MaxBatchMutations)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidComposition);
        }
        JsonData.Identifier(command.Start.Collection);
        JsonData.Identifier(command.Start.Id);
    }

}
