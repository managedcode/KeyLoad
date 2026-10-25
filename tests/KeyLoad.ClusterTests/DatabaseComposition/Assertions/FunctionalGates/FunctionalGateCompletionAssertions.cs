namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Checks actual successful public calls and persisted user identities without estimated credits.</summary>
internal static class FunctionalGateCompletionAssertions
{
    private const int WorkloadPairs = 4;
    private const int WorkloadUsers = 8;
    private const int Resources = 3;
    private const long MinimumSuccessfulPublicCalls = 10_000;
    private const int Empty = 0;

    internal static async Task RequireAsync(FunctionalGateContext[] contexts)
    {
        await Assert.That(contexts.Length).IsEqualTo(WorkloadPairs);
        var writers = contexts.Select(context => context.WriterPrincipalId).ToArray();
        var readers = contexts.Select(context => context.ReaderPrincipalId).ToArray();
        await Assert.That(writers.Concat(readers).Any(string.IsNullOrWhiteSpace)).IsFalse();
        await Assert.That(writers.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(WorkloadPairs);
        await Assert.That(readers.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(WorkloadPairs);
        await Assert.That(writers.Intersect(readers, StringComparer.Ordinal).Count()).IsEqualTo(Empty);
        await Assert.That(writers.Concat(readers).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(WorkloadUsers);
        foreach (var context in contexts)
        {
            await Assert.That(context.ResourceCount).IsEqualTo(Resources);
            await Assert.That(context.SdkOperations).IsGreaterThan((long)Empty);
            await Assert.That(context.McpOperations).IsGreaterThan((long)Empty);
        }
        await Assert.That(contexts.Sum(context => context.CompletedOperations))
            .IsGreaterThanOrEqualTo(MinimumSuccessfulPublicCalls);
    }
}
