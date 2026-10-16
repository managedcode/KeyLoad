namespace KeyLoad.Core.Features.AnalyticsStore;

// Native Text.Value sanitizes text. This explicit disposable projection representation preserves canonical identity.
internal sealed class AnalyticsStoreNativeDimensions
{
    private const int Base64InputPadding = 2;
    private const int Base64InputBytes = 3;
    private const int Base64OutputCharacters = 4;
    private const string Prefix = "b64:";
    private const string Changed = "The native analytics dimension does not match the captured input.";
    private readonly Dictionary<string, Values> values = new(StringComparer.Ordinal);

    internal AnalyticsStoreNativeDimensions(AnalyticsAggregateSnapshot snapshot)
    {
        foreach (var name in snapshot.Request.GroupBy)
        {
            var dimension = new Values();
            values.Add(name, dimension);
            foreach (var row in snapshot.Rows)
            {
                var original = row.Dimensions[name];
                var bytes = AnalyticsStoreDimensionValues.Require(original);
                var encodedLength = checked(Prefix.Length + ((long)bytes + Base64InputPadding) / Base64InputBytes * Base64OutputCharacters);
                if (encodedLength > snapshot.Limits.MaxBatchBytes)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, AnalyticsStoreValidation.Budget);
                }

                snapshot.Work.ChargeBytes(encodedLength);
                if (dimension.Encoded.ContainsKey(original))
                {
                    continue;
                }

                var encoded = Prefix + Convert.ToBase64String(AnalyticsStoreDimensionValues.StrictUtf8.GetBytes(original));
                dimension.Encoded.Add(original, encoded);
                dimension.Original.Add(encoded, original);
            }
        }
    }

    internal string ForEvent(string name, string original)
        => values[name].Encoded.TryGetValue(original, out var encoded) ? encoded
            : throw Errors.Fail(ErrorCode.Corruption, Changed);

    internal string FromResult(string name, string? encoded)
        => encoded is not null && values[name].Original.TryGetValue(encoded, out var original) ? original
            : throw Errors.Fail(ErrorCode.Corruption, Changed);

    private sealed class Values
    {
        internal Dictionary<string, string> Encoded { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string> Original { get; } = new(StringComparer.Ordinal);
    }
}
