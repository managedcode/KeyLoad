#pragma warning disable ORLEANSEXP005
using System.Runtime.CompilerServices;
using KeyLoad.Core;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

namespace KeyLoad.Orleans;

/// <summary>Creates native journal handles backed by the protected replicated runtime journal.</summary>
internal sealed class RuntimeJournalStorageProvider : IJournalStorageProvider, IJournalStorageCatalog
{
    private readonly RuntimeJournalClient client;
    private readonly RuntimeJournalOptions options;
    private readonly IOptions<GrainRoutingOptions> routingOptions;
    private readonly string format;

    internal RuntimeJournalStorageProvider(RuntimeJournalClient client,
        IOptions<RuntimeJournalOptions> configuredOptions,
        IOptions<GrainRoutingOptions> configuredRoutingOptions,
        IOptions<JournaledStateManagerOptions> managerOptions)
    {
        this.client = client;
        options = configuredOptions.Value with { };
        routingOptions = configuredRoutingOptions;
        format = managerOptions.Value.JournalFormatKey;
        if (!options.IsValid() || format != RuntimeJournalStoragePolicy.BinaryFormat)
        {
            throw new InvalidOperationException(RuntimeJournalStoragePolicy.InvalidLimits);
        }
    }

    public IJournalStorage CreateStorage(JournalId journalId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("A runtime journal identifier is required.", nameof(journalId));
        }

        return new RuntimeJournalStorage(client, journalId, options, format, routingOptions);
    }

    public async IAsyncEnumerable<JournalCatalogEntry> ListAsync(ListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prefix = options?.Prefix.Value;
        var minimum = options?.MinId.Value;
        var maximum = options?.MaxId.Value;
        var includeMetadata = options?.IncludeMetadata ?? false;
        var catalog = await client.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
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
            && (minimum is null || string.CompareOrdinal(name, minimum) >= 0)
            && (maximum is null || string.CompareOrdinal(name, maximum) <= 0);
}
#pragma warning restore ORLEANSEXP005
