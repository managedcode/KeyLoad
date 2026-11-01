#pragma warning disable ORLEANSEXP005
using System.Runtime.CompilerServices;
using KeyLoad.Core;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

namespace KeyLoad.Clustering;

/// <summary>Creates execution journal handles backed by the protected replicated runtime journal.</summary>
internal sealed class RuntimeJournalStorageProvider(RuntimeJournalClient client,
    IOptions<RuntimeJournalOptions> configuredOptions,
    IOptions<JournaledStateManagerOptions> managerOptions) : IJournalStorageProvider, IJournalStorageCatalog, IDisposable
{
    private readonly RuntimeJournalClient client = client;
    private readonly IOptions<RuntimeJournalOptions> options = ValidateOptions(configuredOptions);
    private readonly string format = ValidateFormat(managerOptions.Value.JournalFormatKey);
    private int disposed;

    public IJournalStorage CreateStorage(JournalId journalId)
    {
        ObjectDisposedException.ThrowIf(disposed != RuntimeJournalStoragePolicy.NoDisposedProvider, this);
        if (journalId.IsDefault)
        {
            throw new ArgumentException(RuntimeJournalStoragePolicy.RequiredJournalIdentifier, nameof(journalId));
        }

        RuntimeJournalStorageValidation.ValidateName(journalId.Value, options.Value);
        return new RuntimeJournalStorage(client, journalId, options, format);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, RuntimeJournalStoragePolicy.DisposedProvider)
            != RuntimeJournalStoragePolicy.NoDisposedProvider)
        {
            return;
        }

    }

    public async IAsyncEnumerable<JournalCatalogEntry> ListAsync(JournalCatalogListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prefix = (FilterValue(options?.Prefix ?? default)).Value;
        var minimum = (FilterValue(options?.MinId ?? default)).Value;
        var maximum = (FilterValue(options?.MaxId ?? default)).Value;
        var includeMetadata = options?.IncludeMetadata ?? false;
        var catalog = await client.GetCatalogAsync(cancellationToken);
        foreach (var snapshot in catalog.Journals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(snapshot.JournalName, prefix, minimum, maximum))
            {
                continue;
            }

            IJournalMetadata? metadata = includeMetadata
                ? new JournalMetadata(format, snapshot.MetadataETag, snapshot.Properties)
                : null;
            yield return new(new JournalId(snapshot.JournalName), metadata);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool Matches(string name, string? prefix, string? minimum, string? maximum)
        => (prefix is null || name.StartsWith(prefix, StringComparison.Ordinal))
            && (minimum is null || string.CompareOrdinal(name, minimum) >= RuntimeJournalStoragePolicy.EqualOrdinalComparison)
            && (maximum is null || string.CompareOrdinal(name, maximum) <= RuntimeJournalStoragePolicy.EqualOrdinalComparison);

    private static (bool HasValue, string? Value) FilterValue(JournalId id) => id.IsDefault ? (false, default(string?)) : (true, ((string?)(id.Value)));

    private static IOptions<RuntimeJournalOptions> ValidateOptions(IOptions<RuntimeJournalOptions> configuredOptions)
    {
        if (!configuredOptions.Value.IsValid())
        {
            throw new InvalidOperationException(RuntimeJournalStoragePolicy.InvalidLimits);
        }

        return configuredOptions;
    }

    private static string ValidateFormat(string configuredFormat)
    {
        if (configuredFormat != RuntimeJournalStoragePolicy.BinaryFormat)
        {
            throw new InvalidOperationException(RuntimeJournalStoragePolicy.InvalidLimits);
        }

        return configuredFormat;
    }
}
#pragma warning restore ORLEANSEXP005
