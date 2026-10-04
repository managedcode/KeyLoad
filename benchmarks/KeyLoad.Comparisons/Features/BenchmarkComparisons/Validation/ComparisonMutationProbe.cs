namespace KeyLoad.Comparisons;

internal static class ComparisonMutationProbe
{
    internal static async Task VerifyAsync(IComparisonTarget target, BenchmarkDataset dataset, CancellationToken token)
    {
        await using var session = await target.OpenSessionAsync(token);
        using var deadline = ComparisonDeadline.Create(dataset.Options.TimeoutSeconds, token);
        var number = dataset.Options.Documents + 3 * dataset.Options.Repetitions
            * (dataset.Options.Operations + dataset.Options.Warmup) + 17;
        var updated = dataset.CreateDocument(number);
        var deleted = dataset.CreateDocument(number + 1);
        var missing = dataset.CreateDocument(number + 2);
        var initial = BenchmarkDataset.InitialMutationState(Scenario.DocumentUpdate, updated);
        await RequireAbsentAsync(session, updated, deadline.Token);
        await session.ExecuteAsync(Scenario.DocumentWrite, initial, deadline.Token);
        await RequireExactAsync(session, initial, deadline.Token);
        await RequireFailureAsync(session, Scenario.DocumentWrite, initial, ComparisonMutationFailures.CreateConflict, deadline.Token);
        await RequireExactAsync(session, initial, deadline.Token);
        await RequireAbsentAsync(session, missing, deadline.Token);
        await RequireFailureAsync(session, Scenario.DocumentUpdate, missing, ComparisonMutationFailures.UpdateMissing, deadline.Token);
        await RequireFailureAsync(session, Scenario.DocumentDelete, missing, ComparisonMutationFailures.DeleteMissing, deadline.Token);
        await RequireAbsentAsync(session, missing, deadline.Token);
        await session.ExecuteAsync(Scenario.DocumentUpdate, updated, deadline.Token);
        await RequireExactAsync(session, updated, deadline.Token);
        await RequireAbsentAsync(session, deleted, deadline.Token);
        await session.ExecuteAsync(Scenario.DocumentWrite, deleted, deadline.Token);
        await RequireExactAsync(session, deleted, deadline.Token);
        await session.ExecuteAsync(Scenario.DocumentDelete, deleted, deadline.Token);
        await RequireAbsentAsync(session, deleted, deadline.Token);
        await RequireExactAsync(session, updated, deadline.Token);
        await RequireExactAsync(session, dataset.Documents[0], deadline.Token);
    }

    private static async Task RequireFailureAsync(IComparisonSession session, Scenario scenario,
        BenchmarkDocument input, string code, CancellationToken token)
    {
        try
        {
            await session.ExecuteAsync(scenario, input, token);
        }
        catch (ComparisonFailureException error) when (error.Message == code)
        {
            return;
        }
        throw new ComparisonFailureException(ComparisonMutationFailures.ProbeMismatch);
    }

    private static async Task RequireAbsentAsync(IComparisonSession reader, BenchmarkDocument input, CancellationToken token)
    {
        if (await reader.ReadAsync(input, token) is not null)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.ProbeMismatch);
        }
    }

    private static async Task RequireExactAsync(IComparisonSession reader, BenchmarkDocument input, CancellationToken token)
    {
        if (!BenchmarkDataset.SameDocument(await reader.ReadAsync(input, token), input))
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.ProbeMismatch);
        }
    }
}
