namespace KeyLoad.Comparisons;

internal static class ComparisonMutationValidation
{
    private const string SentinelChanged = "MutationSentinelChanged";
    internal static async Task ValidateAsync(IReadOnlyList<IComparisonSession> sessions, BenchmarkDataset dataset,
        Scenario scenario, BenchmarkDocument[] inputs, OperationSample[] samples, OperationResult?[] outputs,
        CancellationToken cancellationToken)
    {
        var next = -1;
        await Task.WhenAll(sessions.Select(async session =>
        {
            while (true)
            {
                var operation = Interlocked.Increment(ref next);
                if (operation >= inputs.Length)
                {
                    return;
                }

                if (!samples[operation].Success)
                {
                    continue;
                }

                try
                {
                    using var deadline = ComparisonDeadline.Create(dataset.Options.TimeoutSeconds, cancellationToken);
                    await ComparisonValidation.ValidateOperationAsync(session, dataset, scenario,
                        inputs[operation], outputs[operation]!, deadline.Token);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                {
                    samples[operation] = samples[operation] with { Success = false, Error = ComparisonErrors.Safe(error) };
                }
            }
        }));
        await VerifySentinelAsync(sessions[0], dataset, samples, cancellationToken);
    }

    private static async Task VerifySentinelAsync(IComparisonSession reader, BenchmarkDataset dataset,
        OperationSample[] samples, CancellationToken token)
    {
        try
        {
            using var deadline = ComparisonDeadline.Create(dataset.Options.TimeoutSeconds, token);
            var sentinel = dataset.Documents[0];
            if (!BenchmarkDataset.SameDocument(await reader.ReadAsync(sentinel, deadline.Token), sentinel))
            {
                throw new ComparisonFailureException(SentinelChanged);
            }
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            for (var operation = 0; operation < samples.Length; operation++)
            {
                if (samples[operation].Success)
                {
                    samples[operation] = samples[operation] with { Success = false, Error = ComparisonErrors.Safe(error) };
                }
            }
        }
    }
}
