#pragma warning disable ORLEANSEXP005
using System.Runtime.CompilerServices;
using KeyLoad.Core;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

namespace KeyLoad.Orleans;

/// <summary>Creates native journal handles backed by the protected replicated runtime journal.</summary>
internal sealed class RuntimeJournalStorageProvider : IJournalStorageProvider, IJournalStorageCatalog, IDisposable
{
    private readonly RuntimeJournalClient client;
    private readonly IOptions<RuntimeJournalOptions> options;
    private readonly IOptions<GrainRoutingOptions> routingOptions;
    private readonly string format;
    private readonly SemaphoreSlim[] gates;
    private int disposed;

    public RuntimeJournalStorageProvider(RuntimeJournalClient client,
        IOptions<RuntimeJournalOptions> configuredOptions,
        IOptions<GrainRoutingOptions> configuredRoutingOptions,
        IOptions<JournaledStateManagerOptions> managerOptions)
    {
        this.client = client;
        options = ValidateOptions(configuredOptions);
        routingOptions = configuredRoutingOptions;
        format = ValidateFormat(managerOptions.Value.JournalFormatKey);
        gates = CreateGates(options.Value.MaximumJournals);
    }

    public IJournalStorage CreateStorage(JournalId journalId)
    {
        ObjectDisposedException.ThrowIf(disposed != RuntimeJournalStoragePolicy.NoDisposedProvider, this);
        if (journalId.IsDefault)
        {
            throw new ArgumentException(RuntimeJournalStoragePolicy.RequiredJournalIdentifier, nameof(journalId));
        }

        RuntimeJournalStorageValidation.ValidateName(journalId.Value, options.Value);
        return new RuntimeJournalStorage(client, journalId, options, format, routingOptions, gates,
            GateIndex(journalId.Value, gates.Length));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, RuntimeJournalStoragePolicy.DisposedProvider)
            != RuntimeJournalStoragePolicy.NoDisposedProvider)
        {
            return;
        }

        foreach (var gate in gates)
        {
            gate.Dispose();
        }
    }

    public async IAsyncEnumerable<JournalCatalogEntry> ListAsync(JournalCatalogListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prefix = FilterValue(options?.Prefix ?? default);
        var minimum = FilterValue(options?.MinId ?? default);
        var maximum = FilterValue(options?.MaxId ?? default);
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
            && (minimum is null || string.CompareOrdinal(name, minimum) >= RuntimeJournalStoragePolicy.EqualOrdinalComparison)
            && (maximum is null || string.CompareOrdinal(name, maximum) <= RuntimeJournalStoragePolicy.EqualOrdinalComparison);

    private static string? FilterValue(JournalId id) => id.IsDefault ? null : id.Value;

    private static SemaphoreSlim[] CreateGates(int count)
    {
        var values = new SemaphoreSlim[count];
        for (var index = RuntimeJournalStoragePolicy.InitialGateIndex; index < count; index++)
        {
            values[index] = new(RuntimeJournalStoragePolicy.AvailableHandleGatePermits,
                RuntimeJournalStoragePolicy.MaximumHandleGatePermits);
        }

        return values;
    }

    private static int GateIndex(string journalName, int gateCount)
        => (int)((uint)StringComparer.Ordinal.GetHashCode(journalName) % (uint)gateCount);

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
