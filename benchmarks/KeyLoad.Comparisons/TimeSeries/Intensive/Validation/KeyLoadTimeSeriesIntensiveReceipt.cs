namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal static class KeyLoadTimeSeriesIntensiveReceipt
{
    internal static (TimeSeriesIntensiveAppendReceipt Receipt, long Position) Validate(CommitReceipt? actual,
        Guid expectedCommandId, Guid expectedIncarnation, string expectedPartitionId, string expectedSet,
        string expectedSeries, long? expectedRevision, int expectedCopies)
    {
        var mutation = (MatchingMutation(actual, expectedSet, expectedSeries)).Value;
        long? observedSequence = mutation is not null
            && mutation.Revision >= KeyLoadTimeSeriesIntensiveProtocol.MinimumPositiveReceiptValue
            ? mutation.Revision : null;
        if (actual is null || actual.CommandId != expectedCommandId || mutation is null
            || mutation.Revision < KeyLoadTimeSeriesIntensiveProtocol.MinimumPositiveReceiptValue
            || expectedRevision is { } terminal && mutation.Revision != terminal
            || !ValidToken(actual.Token, expectedIncarnation, expectedPartitionId)
            || actual.Durability != DurabilityProfile.ProcessDurable || actual.PersistedCopies != expectedCopies)
        {
            throw new KeyLoadTimeSeriesIntensiveReplyException(observedSequence);
        }

        return (new(actual.CommandId, mutation.Revision), actual.Token.Position);
    }

    private static (bool HasValue, MutationReceipt? Value) MatchingMutation(CommitReceipt? actual, string expectedSet,
        string expectedSeries)
    {
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;

        if (actual is null || actual.Mutations.IsDefault || actual.Mutations.Length != SingleItemCount)
        {
            return (false, default(MutationReceipt?));
        }

        var mutation = actual.Mutations[FirstElementIndex];
        return mutation is not null && mutation.Kind == KeyLoadTimeSeriesIntensiveProtocol.AppendSamplesKind
            && mutation.Resource == expectedSet && mutation.Id == expectedSeries
            ? (true, ((MutationReceipt?)(mutation))) : (false, default(MutationReceipt?));
    }

    private static bool ValidToken(CommitToken? token, Guid incarnation, string partitionId) =>
        token is not null && token.Incarnation == incarnation
        && token.AtomicPartitionId == partitionId
        && token.Position >= KeyLoadTimeSeriesIntensiveProtocol.MinimumPositiveReceiptValue;
}
