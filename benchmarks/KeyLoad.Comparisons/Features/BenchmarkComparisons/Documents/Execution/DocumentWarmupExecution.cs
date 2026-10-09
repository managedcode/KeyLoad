using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal sealed class DocumentWarmupExecution(IOptions<NativeComparisonExecutionOptions> options, TimeProvider clock)
{
    private readonly NativeComparisonExecutionOptions execution = NativeComparisonExecutionOptions.Require(options).Value;
    private IComparisonTarget? target;
    private IDocumentComparisonSession? session;
    internal async Task RunWarmupAsync(Func<int, CancellationToken, Task<IComparisonTarget>> factory,
        DocumentComparisonSelection selection, int index, CancellationToken token)
    {
        var cleanup = ImmutableArray.CreateBuilder<Exception>();
        async Task ExecuteAsync()
        {
            var count = DocumentComparisonContract.Current.Warmup;
            var initial = selection.Scenario == DocumentComparisonScenario.Ingest ? DocumentMeasurementValues.NoObservedItems : count * DocumentMeasurementValues.WarmupCorpusMultiplier;
            var corpus = new DocumentComparisonCorpus(initial, selection.Clients);
            var schedule = new DocumentComparisonSchedule(selection.Scenario, corpus, count);
            target = await factory(index, token).ConfigureAwait(false);
            if (target is not IDocumentComparisonTarget native)
            {
                throw new NotSupportedException();
            }

            await native.InitializeDocumentsAsync(new(corpus, initial != DocumentMeasurementValues.NoObservedItems, schedule.MaximumIdentityExclusive), token).ConfigureAwait(false);
            session = await native.OpenDocumentSessionAsync(token).ConfigureAwait(false);
            await ExecuteWarmupAsync(session, schedule, count, token).ConfigureAwait(false);
        }

        var primary = await OpenLoopFailure.ObserveAsync(ExecuteAsync()).ConfigureAwait(false);
        if (session is not null)
        {
            var settled = await ComparisonSessionCleanup.CloseAndJoinAsync([session], execution.CleanupTimeout, timeProvider: clock).ConfigureAwait(false);
            cleanup.AddRange(settled.Failures);
            if (settled.ThresholdExpired)
            {
                cleanup.Add(new ComparisonFailureException(DocumentProtocolText.DocumentClientCleanupThresholdExpired));
            }
        }
        if (target is not null)
        {
            if (await OpenLoopFailure.ObserveAsync(DisposeTargetAsync(target)).ConfigureAwait(false) is { } failure)
            {
                cleanup.Add(failure);
            }
        }
        var combined = OpenLoopFailure.Combine(primary, cleanup.ToImmutable());
        if (combined is not null)
        {
            ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }

    private async Task ExecuteWarmupAsync(IDocumentComparisonSession session, DocumentComparisonSchedule schedule,
        int count, CancellationToken token)
    {
        for (var ordinal = DocumentMeasurementValues.NoObservedItems; ordinal < count; ordinal++)
        {
            using var deadline = ComparisonDeadline.Create(execution.OperationTimeout, cancellationToken: token, timeProvider: clock);
            var work = schedule.At(ordinal);
            var response = await session.ExecuteAsync(work.Operation, work.Input, deadline.Token).ConfigureAwait(false);
            ScaledComparisonMeasurementExecutor.ValidatePointRead(work.Operation, response, work.Input);
        }
        _ = await DocumentComparisonOracle.VerifyAsync(session.ReadDocumentsAsync(token), schedule.FinalDocuments(), token).ConfigureAwait(false);
    }
    private static async Task DisposeTargetAsync(IComparisonTarget target) => await target.DisposeAsync().ConfigureAwait(false);
}
