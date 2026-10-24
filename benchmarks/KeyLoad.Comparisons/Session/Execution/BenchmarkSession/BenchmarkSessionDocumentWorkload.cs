using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionDocumentWorkload(BenchmarkSessionContext context)
{
    internal async Task<BenchmarkSessionCaseReport> LoadAsync(CancellationToken token)
    {
        var state = context.State;
        if (state.Loaded || state.Poisoned)
        {
            return state.Report(BenchmarkSessionProtocol.LoadMillion, BenchmarkSessionProtocol.Failed, BenchmarkSessionProtocol.SessionLoadAlreadyAttempted, BenchmarkSessionLimits.NoObservedItems, [], BenchmarkSessionLimits.NoObservedItems);
        }
        if (context.Target is not IDocumentComparisonTarget execution || !context.Target.Supports(Scenario.DocumentWrite))
        {
            state.UnsupportedDocumentContract = context.Target.UnsupportedReason;
            return state.Unavailable(BenchmarkSessionProtocol.LoadMillion, state.UnsupportedDocumentContract);
        }
        var timer = new BenchmarkSessionTimer(context.Clock);
        ImmutableArray<BenchmarkSessionLaneReport> retained = [];
        try
        {
            // Every client loads through the database's own bulk API at the vendor's recommended size; each still gets requests.
            var batch = execution.IngestionBatchRecords;
            if (batch < BenchmarkSessionLimits.SingleItemCount || batch > context.Options.Records / context.Options.Concurrency)
            {
                throw new ComparisonFailureException(BenchmarkSessionProtocol.IngestionBatchOutsideClientShare);
            }
            await execution.InitializeDocumentsAsync(new(new DocumentComparisonCorpus(BenchmarkSessionLimits.NoObservedItems, context.Options.Concurrency), false,
                checked(context.Options.Records + context.Options.Operations)), token).ConfigureAwait(false);
            var lane = await context.Measurement.MeasureBatchesAsync(BenchmarkSessionProtocol.Create, context.Options.Records, context.Options.Concurrency,
                batch, (client, first, length, cancellation) => ((IDocumentComparisonSession)client).CreateDocumentsAsync(
                    [.. Enumerable.Range(first, length).Select(context.Corpus.CreateDocument)], cancellation), token).ConfigureAwait(false);
            retained = [lane];
            if (lane.Measurement.Failures != BenchmarkSessionLimits.NoObservedItems || lane.Measurement.Attempts != context.Options.Records)
            {
                return state.Failed(BenchmarkSessionProtocol.LoadMillion, BenchmarkSessionProtocol.InitialLoadAcknowledgementsIncomplete, timer, retained);
            }
            var proof = await context.Verification.VerifyAsync(false, token).ConfigureAwait(false);
            state.Loaded = true;
            return state.Report(BenchmarkSessionProtocol.LoadMillion, BenchmarkSessionProtocol.Measured, null, proof.Records, retained, lane.Measurement.ElapsedSeconds);
        }
        catch (Exception error) when (BenchmarkSessionFailure.IsNonfatal(error))
        {
            return state.Failed(BenchmarkSessionProtocol.LoadMillion, ComparisonErrors.Safe(error), timer, retained);
        }
    }

    internal async Task<BenchmarkSessionCaseReport> VerifyAsync(CancellationToken token)
    {
        var state = context.State;
        var afterMixed = context.Target.Supports(Scenario.DocumentDelete);
        if (!state.Ready || afterMixed && !state.Mixed)
        {
            return state.Prerequisite(BenchmarkSessionProtocol.VerifyFinal);
        }
        var timer = new BenchmarkSessionTimer(context.Clock);
        try
        {
            var proof = await context.Verification.VerifyAsync(afterMixed, token).ConfigureAwait(false);
            return state.Report(BenchmarkSessionProtocol.VerifyFinal, BenchmarkSessionProtocol.Measured, null, proof.Records, [], timer.Elapsed.TotalSeconds);
        }
        catch (Exception error) when (BenchmarkSessionFailure.IsNonfatal(error))
        {
            return state.Failed(BenchmarkSessionProtocol.VerifyFinal, ComparisonErrors.Safe(error), timer, []);
        }
    }
}
