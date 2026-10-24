using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal sealed class DistributedSearchOperation(DatabaseEngine database, DistributedSearchRequest request,
    ImmutableArray<PartitionRef> partitions, IOptions<QueryExecutionOptions> options,
    ReadExecutionBudget budget, ReadExecutionBudgetReadGrant[] grants,
    Func<DistributedSearchPhaseWork, ReadExecutionBudgetReadGrant, ReadExecutionBudget, CancellationToken,
        Task<DistributedSearchPreparedLeaf>> prepare,
    Func<CancellationToken, Task> sourceBarrier, CancellationTokenSource execution,
    Func<CancellationToken, Task>? statisticsCaptured)
{
    private const int FirstLeaf = 0;
    private const string ScoreProfile = "distributed-canonical";

    internal async Task<DistributedSearchPage> RunAsync()
    {
        var initial = DistributedSearchWorkFactory.Create(DistributedSearchPhase.Statistics, partitions,
            request.Search, default, null, null, default, default, budget);
        var captured = await RunAsync(DistributedSearchPhase.Statistics, initial).ConfigureAwait(true);
        var witnesses = Witnesses(captured);
        DistributedSearchStatisticsRequestBinding.Require(request.Search, witnesses, options, budget);
        var digest = DistributedSearchRequestDigest.Create(request, budget);
        var epoch = DistributedSearchStatisticsEpoch.Create(digest, witnesses, options, budget);
        var scope = new GlobalBranchScope(ScoreProfile, digest, epoch);
        var statistics = Statistics(witnesses);
        var windows = DistributedSearchWorkFactory.WindowIds(partitions, budget);
        if (statisticsCaptured is not null)
        {
            await statisticsCaptured(execution.Token).ConfigureAwait(true);
            budget.Check();
        }
        var candidateWork = DistributedSearchWorkFactory.Create(DistributedSearchPhase.Candidates, partitions,
            request.Search, witnesses, statistics, scope, windows, default, budget);
        var candidates = await RunAsync(DistributedSearchPhase.Candidates, candidateWork).ConfigureAwait(true);
        var selected = DistributedSearchGlobalFusion.Select(request.Search,
            Branches(candidates, text: true), Branches(candidates, text: false), windows, scope,
            database.Limits, budget, out var fusion);
        var projectionWork = DistributedSearchWorkFactory.Create(DistributedSearchPhase.Projection, partitions,
            request.Search, witnesses, null, null, default, selected, budget);
        var projected = await RunAsync(DistributedSearchPhase.Projection, projectionWork).ConfigureAwait(true);
        await sourceBarrier(execution.Token).ConfigureAwait(true);
        budget.Check();
        var finalWork = DistributedSearchWorkFactory.Create(DistributedSearchPhase.Revalidate, partitions,
            request.Search, witnesses, null, null, default, default, budget);
        _ = await RunAsync(DistributedSearchPhase.Revalidate, finalWork).ConfigureAwait(true);
        budget.Check();
        return DistributedSearchPublicMapper.Map(selected, projected, witnesses, epoch,
            fusion, request.Search.Explain, budget);
    }

    private Task<ImmutableArray<DistributedSearchLeafResult>> RunAsync(DistributedSearchPhase phase,
        ImmutableArray<DistributedSearchPhaseWork> work)
        => DistributedSearchPhaseBatch.RunAsync(work, grants, DistributedSearchGrants.Index(phase,
            FirstLeaf, partitions.Length), options, budget, prepare, execution);

    private ImmutableArray<DistributedTextWitness> Witnesses(ImmutableArray<DistributedSearchLeafResult> results)
    {
        budget.ChargeBytes(PartitionQueryRetention.CandidateArrayBytes(results.Length));
        var witnesses = ImmutableArray.CreateBuilder<DistributedTextWitness>(results.Length);
        foreach (var result in results)
        {
            budget.Check();
            witnesses.Add(result.Witness);
        }
        return witnesses.MoveToImmutable();
    }

    private DistributedTextStatistics Statistics(ImmutableArray<DistributedTextWitness> witnesses)
    {
        budget.ChargeBytes(PartitionQueryRetention.CandidateArrayBytes(witnesses.Length));
        var summaries = ImmutableArray.CreateBuilder<DistributedTextStatistics>(witnesses.Length);
        foreach (var witness in witnesses)
        {
            budget.Check();
            summaries.Add(witness.Statistics);
        }
        return DistributedTextStatisticsMerge.Combine(summaries.MoveToImmutable(), options, budget);
    }

    private ImmutableArray<GlobalBranchWindow> Branches(ImmutableArray<DistributedSearchLeafResult> results, bool text)
    {
        if (text ? request.Search.Text is null : request.Search.Vector is null)
        {
            return [];
        }
        budget.ChargeBytes(PartitionQueryRetention.CandidateArrayBytes(results.Length));
        var windows = ImmutableArray.CreateBuilder<GlobalBranchWindow>(results.Length);
        foreach (var result in results)
        {
            budget.Check();
            var branch = text ? result.Candidates!.Text : result.Candidates!.Vector;
            windows.Add(branch ?? throw Errors.Fail(ErrorCode.Corruption, DistributedSearchPhaseErrors.InvalidBatch));
        }
        return windows.MoveToImmutable();
    }
}
