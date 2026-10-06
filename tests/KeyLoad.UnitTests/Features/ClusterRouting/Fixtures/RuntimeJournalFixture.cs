using System.Collections.Immutable;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RuntimeJournalFixture : IDisposable
{
    internal const string JournalPrincipal = "keyload-internal-runtime-journal";
    private const string RootPrincipal = "root";
    private readonly TestDatabase database;

    internal RuntimeJournalFixture(RuntimeJournalOptions? configured = null)
    {
        database = new TestDatabase();
        var options = configured ?? new RuntimeJournalOptions();
        if (!options.IsValid())
        {
            throw new ArgumentException(RuntimeJournalOptions.ValidationMessage, nameof(configured));
        }

        database.Store.RequireReaderContract(KeyLoad.Storage.StoreReaderContract.RuntimeJournal);
        database.Database.ConfigureRuntimeJournal(Options.Create(options));
        Bootstrap();
    }

    internal DatabaseEngine Engine => database.Database;

    internal RuntimeJournalMutationResult Submit(RuntimeJournalMutation mutation, string principal = JournalPrincipal)
        => database.Submit(OperationKind.RuntimeJournal, mutation, principal).Get<RuntimeJournalMutationResult>();

    internal OperationResult SubmitOperation<T>(OperationKind kind, T payload, string principal = RootPrincipal)
        => database.Submit(kind, payload, principal);

    internal RuntimeJournalSnapshot Create(string name, string instance, Dictionary<string, string>? properties = null)
        => Submit(new(RuntimeJournalAction.Create, name, Guid.Parse(instance), 0, 0, null, ReadOnlyMemory<byte>.Empty,
            properties ?? new(StringComparer.Ordinal), [])).Snapshot!;

    internal RuntimeJournalMutationResult Bootstrap()
        => Submit(new(RuntimeJournalAction.BootstrapIdentity, string.Empty, Guid.Empty, 0, 0, null,
            ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []), RootPrincipal);

    internal static RuntimeJournalMutation Mutation(RuntimeJournalAction action, RuntimeJournalSnapshot snapshot,
        byte[]? data = null, string? etag = null, Dictionary<string, string>? set = null, string[]? remove = null)
        => new(action, snapshot.JournalName, snapshot.InstanceId, snapshot.OwnerGeneration, snapshot.ContentRevision,
            etag, data is null ? ReadOnlyMemory<byte>.Empty : data, set ?? new(StringComparer.Ordinal),
            remove?.ToImmutableArray() ?? []);

    public void Dispose() => database.Dispose();
}
