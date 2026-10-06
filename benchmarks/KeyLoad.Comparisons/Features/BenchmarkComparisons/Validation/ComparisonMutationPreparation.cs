namespace KeyLoad.Comparisons;

internal static class ComparisonMutationPreparation
{
    private const string InitialStateMismatch = "MutationInitialStateMismatch";
    internal static bool Required(Scenario scenario)
        => scenario is Scenario.DocumentUpdate or Scenario.DocumentDelete;

    internal static async Task PrepareAsync(IReadOnlyList<IComparisonSession> sessions, BenchmarkDocument[] inputs,
        Scenario scenario, int timeoutSeconds, CancellationToken cancellationToken)
    {
        const int WarmupRepetitionIndex = -1;

        if (!Required(scenario))
        {
            return;
        }

        var next = WarmupRepetitionIndex;
        await Task.WhenAll(sessions.Select(async session =>
        {
            while (true)
            {
                var operation = Interlocked.Increment(ref next);
                if (operation >= inputs.Length)
                {
                    return;
                }

                using var deadline = ComparisonDeadline.Create(timeoutSeconds, cancellationToken);
                var initial = BenchmarkDataset.InitialMutationState(scenario, inputs[operation]);
                await session.ExecuteAsync(Scenario.DocumentWrite, initial, deadline.Token);
                if (!BenchmarkDataset.SameDocument(await session.ReadAsync(initial, deadline.Token), initial))
                {
                    throw new ComparisonFailureException(InitialStateMismatch);
                }
            }
        }));
    }
}
