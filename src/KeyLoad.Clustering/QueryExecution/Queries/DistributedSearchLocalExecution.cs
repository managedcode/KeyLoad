using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Runs bounded query phases after Orleans has addressed the canonical storage owner.</summary>
internal sealed class DistributedSearchLocalExecution(DatabaseEngine database, string principalId,
    PhysicalShardRecord owner, IOptions<QueryExecutionOptions> options, TimeProvider clock,
    DateTimeOffset expiry, IResourceLifetimeWorkBinding binding)
{
    private const int ContractVersion = 1;

    internal DistributedSearchPreparedLeaf Prepare(DistributedSearchPhaseWork phase,
        ReadExecutionBudgetReadGrant grant, ReadExecutionBudget parent)
    {
        parent.Check();
        var original = new DistributedSearchOwnedLeafV1(ContractVersion, phase.Phase, phase.Search,
            owner, phase.Search.Partition.TenantId, grant.RemainingBytes, grant.RemainingRecords,
            phase.MaxResultBytes, phase.Witness, phase.Statistics, phase.Scope,
            phase.SourceWindowId, phase.Selected);
        var request = database.WithQueryView(principalId, phase.Search.Partition, phase.Search.Collection,
            Capability.None, (view, principal, definition) => new DistributedSearchOwnedLeafV2(original,
                definition, parent.BindResourceLifetimes(database, view, phase.Search.Partition, [definition]),
                principal), parent);
        return new(original, token => Task.FromResult(DistributedSearchOwnedLeafExecution.ExecuteV2Bound(
            database, principalId, request, owner, options, clock, expiry, binding, token)),
            (result, token) => Complete(parent, grant, result, token));
    }

    private static Task Complete(ReadExecutionBudget parent, ReadExecutionBudgetReadGrant grant,
        DistributedSearchLeafResultV1 result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        parent.ImportReadGrant(grant, result.ReadBytes, result.ExaminedRecords);
        parent.Check();
        return Task.CompletedTask;
    }
}
