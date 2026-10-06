using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal static class ScaledComparisonOperationSetup
{
    internal static async Task<List<IComparisonSession>> OpenSessionsAsync(IComparisonTarget target, int count, IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken token)
    {
        const int FirstElementIndex = 0;

        var execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
        var sessions = new List<IComparisonSession>(count);
        try
        {
            for (var index = FirstElementIndex; index < count; index++)
            {
                sessions.Add(await target.OpenSessionAsync(token).ConfigureAwait(false));
            }
            return sessions;
        }
        catch (Exception)
        {
            _ = await ComparisonSessionCleanup.CloseAsync(sessions, execution.CleanupTimeout).ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task PrepareAsync(List<IComparisonSession> sessions, ScaledOperationInputs inputs,
        IComparisonSettings settings, CancellationToken token)
    {
        const int FirstElementIndex = 0;

        if (inputs.Scenario is not (Scenario.DocumentUpdate or Scenario.DocumentDelete))
        {
            return;
        }
        await Parallel.ForEachAsync(Enumerable.Range(FirstElementIndex, sessions.Count),
            new ParallelOptions { MaxDegreeOfParallelism = settings.Concurrency, CancellationToken = token },
            async (sessionIndex, cancellationToken) =>
                await PrepareSessionAsync(sessions[sessionIndex], sessionIndex, sessions.Count, inputs, settings, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    private static async Task PrepareSessionAsync(IComparisonSession session, int sessionIndex, int sessionCount,
        ScaledOperationInputs inputs, IComparisonSettings settings, CancellationToken cancellationToken)
    {
        var operationCount = settings.Operations + settings.Warmup;
        for (var ordinal = sessionIndex; ordinal < operationCount; ordinal += sessionCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var warmup = ordinal < settings.Warmup;
            var operation = warmup ? ordinal : ordinal - settings.Warmup;
            var input = inputs.Create(operation, warmup);
            var initial = inputs.Scenario == Scenario.DocumentUpdate
                ? BenchmarkDataset.InitialMutationState(Scenario.DocumentUpdate, input) : input;
            await session.ExecuteAsync(Scenario.DocumentWrite, initial, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task WarmupAsync(List<IComparisonSession> sessions, ScaledOperationInputs inputs,
        IComparisonSettings settings, TimeSpan operationTimeout, CancellationToken token)
    {
        const int FirstElementIndex = 0;

        for (var operation = FirstElementIndex; operation < settings.Warmup; operation++)
        {
            token.ThrowIfCancellationRequested();
            using var deadline = ComparisonDeadline.Create(operationTimeout, token);
            var input = inputs.Create(operation, warmup: true);
            var result = await sessions[operation % sessions.Count].ExecuteAsync(inputs.Scenario, input, deadline.Token).ConfigureAwait(false);
            ScaledComparisonMeasurementExecutor.ValidatePointRead(inputs.Scenario, result, input);
        }
    }

    internal static async Task VerifyMutationResultsAsync(List<IComparisonSession> sessions,
        ScaledOperationInputs inputs, IComparisonSettings settings, CancellationToken token)
    {
        const int FirstElementIndex = 0;

        if (inputs.Scenario == Scenario.PointRead)
        {
            return;
        }
        await Parallel.ForEachAsync(Enumerable.Range(FirstElementIndex, sessions.Count),
            new ParallelOptions { MaxDegreeOfParallelism = settings.Concurrency, CancellationToken = token },
            async (sessionIndex, cancellationToken) =>
                await VerifySessionAsync(sessions[sessionIndex], sessionIndex, sessions.Count, inputs,
                    settings.Operations, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static async Task VerifySessionAsync(IComparisonSession session, int sessionIndex, int sessionCount,
        ScaledOperationInputs inputs, int operationCount, CancellationToken cancellationToken)
    {
        const string ScaledMutationReadbackMismatchDetail = "ScaledMutationReadbackMismatch";

        for (var operation = sessionIndex; operation < operationCount; operation += sessionCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = inputs.Create(operation, warmup: false);
            var actual = await session.ReadAsync(input, cancellationToken).ConfigureAwait(false);
            if (inputs.Scenario == Scenario.DocumentDelete ? actual is not null : !BenchmarkDataset.SameDocument(actual, input))
            {
                throw new ComparisonFailureException(ScaledMutationReadbackMismatchDetail);
            }
        }
    }
}
