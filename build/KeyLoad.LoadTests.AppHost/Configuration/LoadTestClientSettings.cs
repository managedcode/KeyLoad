namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

/// <summary>Retains typed Aspire references for the client that owns workload execution outside the resource graph.</summary>
internal sealed class LoadTestClientSettings
{
    private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, object> Values => values;
    internal HashSet<string> Resources { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> CompletionResources { get; } = new(StringComparer.Ordinal);

    internal void Set(string name, string value) => values.Add(name, value);

    internal void Set(string name, IValueProvider value) => values.Add(name, value);

    internal async Task<IReadOnlyDictionary<string, string>> ResolveAsync(CancellationToken token)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            token.ThrowIfCancellationRequested();
            resolved.Add(name, value is IValueProvider reference
                ? await reference.GetValueAsync(token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The selected database reference is unavailable.")
                : (string)value);
        }
        return resolved;
    }
}
