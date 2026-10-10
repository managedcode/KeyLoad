using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class DistributedSearchOperation(DatabaseEngine database, DistributedSearchRequestV1 request,
    ImmutableArray<PartitionRef> partitions, IOptions<QueryExecutionOptions> options,
    ReadExecutionBudget budget, ReadExecutionBudgetReadGrant[] grants,
    Func<DistributedSearchPhaseWork, ReadExecutionBudgetReadGrant, ReadExecutionBudget, DistributedSearchPreparedLeaf> prepare,
    Func<CancellationToken, Task> sourceBarrier, CancellationTokenSource execution,
    Func<CancellationToken, Task>? statisticsCaptured)
{
    private const int FirstLeaf = 0;
    private const string ScoreProfile = "distributed-canonical-v1";

    internal async Task<DistributedSearchPageV1> RunAsync()
    {
        var initial = DistributedSearchWorkFactory.Create(DistributedSearchPhase.Statistics, partitions,
            request.Search, default, null, null, default, default, budget);
        var captured = await RunAsync(DistributedSearchPhase.Statistics, initial).ConfigureAwait(false);
        var witnesses = Witnesses(captured);
        DistributedSearchStatisticsRequestBinding.Require(request.Search, witnesses, options, budget);
        var digest = DistributedSearchRequestDigest.Create(request, budget);
        var epoch = DistributedSearchStatisticsEpoch.Create(digest, witnesses, options, budget);
        var scope = new GlobalBranchScope(ScoreProfile, digest, epoch);
        var statistics = Statistics(witnesses);
        var windows = DistributedSearchWorkFactory.WindowIds(partitions, budget);
        if (statisticsCaptured is not null)
        { await statisticsCaptured(execution.Token).ConfigureAwait(false); budget.Check(); }
        var candidateWork = DistributedSearchWorkFactory.Create(DistributedSearchPhase.Candidates, partitions,
            request.Search, witnesses, statistics, scope, windows, default, budget);
        var candidates = await RunAsync(DistributedSearchPhase.Candidates, candidateWork).ConfigureAwait(false);
        var selected = DistributedSearchGlobalFusion.Select(request.Search,
            Branches(candidates, text: true), Branches(candidates, text: false), windows, scope,
            database.Limits, budget, out var fusion);
        var projectionWork = DistributedSearchWorkFactory.Create(DistributedSearchPhase.Projection, partitions,
            request.Search, witnesses, null, null, default, selected, budget);
        var projected = await RunAsync(DistributedSearchPhase.Projection, projectionWork).ConfigureAwait(false);
        await sourceBarrier(execution.Token).ConfigureAwait(false);
        budget.Check();
        var finalWork = DistributedSearchWorkFactory.Create(DistributedSearchPhase.Revalidate, partitions,
            request.Search, witnesses, null, null, default, default, budget);
        _ = await RunAsync(DistributedSearchPhase.Revalidate, finalWork).ConfigureAwait(false);
        budget.Check();
        return DistributedSearchPublicMapper.Map(selected, projected, witnesses, epoch,
            fusion, request.Search.Explain, budget);
    }

    private Task<ImmutableArray<DistributedSearchLeafResultV1>> RunAsync(DistributedSearchPhase phase,
        ImmutableArray<DistributedSearchPhaseWork> work)
        => DistributedSearchPhaseBatch.RunAsync(work, grants, DistributedSearchGrants.Index(phase,
            FirstLeaf, partitions.Length), options, budget, prepare, execution);

    private ImmutableArray<DistributedTextWitnessV1> Witnesses(ImmutableArray<DistributedSearchLeafResultV1> results)
    {
        budget.ChargeBytes(PartitionQueryRetention.CandidateArrayBytes(results.Length));
        var witnesses = ImmutableArray.CreateBuilder<DistributedTextWitnessV1>(results.Length);
        foreach (var result in results)
        { budget.Check(); witnesses.Add(result.Witness); }
        return witnesses.MoveToImmutable();
    }

    private DistributedTextStatisticsV1 Statistics(ImmutableArray<DistributedTextWitnessV1> witnesses)
    {
        budget.ChargeBytes(PartitionQueryRetention.CandidateArrayBytes(witnesses.Length));
        var summaries = ImmutableArray.CreateBuilder<DistributedTextStatisticsV1>(witnesses.Length);
        foreach (var witness in witnesses)
        { budget.Check(); summaries.Add(witness.Statistics); }
        return DistributedTextStatisticsMerge.Combine(summaries.MoveToImmutable(), options, budget);
    }

    private ImmutableArray<GlobalBranchWindow> Branches(ImmutableArray<DistributedSearchLeafResultV1> results, bool text)
    {
        if (text ? request.Search.Text is null : request.Search.Vector is null)
        { return []; }
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
