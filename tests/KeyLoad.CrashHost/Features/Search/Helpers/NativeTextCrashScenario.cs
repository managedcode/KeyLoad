using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextCrashScenario
{
    private const int ModeArgumentFromEnd = 1;

    internal const string Mode = "native-text-projection-process";
    private const int ArgumentCount = 5;
    private const string FirstDocumentId = "native-a";
    private const string SecondDocumentId = "native-b";
    private static readonly string[] CanonicalDocumentIds = [FirstDocumentId, SecondDocumentId];
    private const string TextPath = "/text";
    private const string InitialQuery = "needle";
    private const string ReplacementQuery = "changed";
    private const string FirstDocumentJson = "{\"text\":\"needle\"}";
    private const string SecondDocumentJson = "{\"text\":\"needle\"}";
    private const string ReplacementDocumentJson = "{\"text\":\"changed\"}";
    private const string OwnerLockName = "owner.lock";
    private const string ChangedStageMessage = "The requested native text stage was not reached.";
    private const string DatabaseCommandId = "16d05be2-6c50-40e4-b1e9-3e28b52a35d0";
    private const string SeedCommandId = "dd293cea-ad3e-46d4-9ae3-0d50acb3c496";
    private const string ReplacementCommandId = "2719f48a-d7d1-4f8d-a3f1-8e9f2648e969";

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        const int ArgsFirstIndex = 0;
        const int ArgsSecondIndex = 1;
        const int ArgsComponentIndex = 2;
        const int TryRunAsyncArgsComponentIndex = 3;

        ArgumentNullException.ThrowIfNull(args);
        if (args.Length != ArgumentCount || !string.Equals(args[ModeArgumentFromEnd], Mode, StringComparison.Ordinal))
        {
            return false;
        }
        await RunAsync(args[ArgsFirstIndex], args[ArgsSecondIndex], args[ArgsComponentIndex], bool.Parse(args[TryRunAsyncArgsComponentIndex]));
        return true;
    }

    private static async Task RunAsync(string directory, string receiptPath, string stageText, bool replacement)
    {
        const string RunAsyncMessageText = "The native text crash stage is unsupported.";
        const string RunAsyncPath2Text = "search-indexes";

        var stage = Enum.Parse<NativeTextFaultStage>(stageText, ignoreCase: false);
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stageText), stageText, RunAsyncMessageText);
        }
        using var store = new ZoneTreeStore(new(directory), CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        var database = CrashDatabase.Create(store);
        var partition = CreatePartition();
        SeedDatabase(database, partition);
        var boundary = new NativeTextCrashBoundary(stage) { Armed = !replacement };
        using var projection = new NativeTextProjection(Path.Combine(directory, RunAsyncPath2Text),
            CrashExecutionOptions.DatabaseLimits(database.Limits), store.Identity.NodeId, CrashExecutionOptions.NativeText(),
            faultObserver: boundary.Observe);
        var search = new SearchEngine(database, CrashExecutionOptions.QueryExecution(), projection);
        if (replacement)
        {
            _ = await search.SearchAsync(CrashFixtureValues.Principal,
                new(partition, CrashFixtureValues.Orders, TextPath, InitialQuery)).ConfigureAwait(false);
            ReplaceFirstDocument(database, partition);
        }

        var query = replacement ? ReplacementQuery : InitialQuery;
        string[] expectedIds = replacement ? [FirstDocumentId] : [FirstDocumentId, SecondDocumentId];
        var receipt = await CaptureReceiptAsync(directory, store, database, partition, query, expectedIds,
            CanonicalDocumentIds);
        await File.WriteAllBytesAsync(receiptPath, NativeSerialization.Serialize(receipt));
        boundary.Armed = true;
        _ = await search.SearchAsync(CrashFixtureValues.Principal,
            new(partition, CrashFixtureValues.Orders, TextPath, query)).ConfigureAwait(false);
        if (!boundary.Observed)
        {
            throw new InvalidOperationException(ChangedStageMessage);
        }
    }

    private static PartitionRef CreatePartition()
        => new(CrashFixtureValues.Tenant, CrashFixtureValues.Database, CrashFixtureValues.Orders,
            CrashFixtureValues.Partition);

    private static void SeedDatabase(DatabaseEngine database, PartitionRef partition)
    {
        CrashDatabase.Submit(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
                new(CrashFixtureValues.Orders, ResourceKind.Collection, partition.TransactionDomainId)),
            Guid.Parse(DatabaseCommandId)).Get<ResourceDefinition>();
        var command = new CommandRequest(Guid.Parse(SeedCommandId), partition,
        [
            new PutDocument(CrashFixtureValues.Orders, FirstDocumentId, FirstDocumentJson),
            new PutDocument(CrashFixtureValues.Orders, SecondDocumentId, SecondDocumentJson)
        ]);
        CrashDatabase.Submit(database, OperationKind.Batch, command, command.CommandId).Get<CommitReceipt>();
    }

    private static void ReplaceFirstDocument(DatabaseEngine database, PartitionRef partition)
    {
        const int ExpectedRevisionSingleItemCount = 1;

        var command = new CommandRequest(Guid.Parse(ReplacementCommandId), partition,
        [new PutDocument(CrashFixtureValues.Orders, FirstDocumentId, ReplacementDocumentJson, ExpectedRevision: ExpectedRevisionSingleItemCount)]);
        CrashDatabase.Submit(database, OperationKind.Batch, command, command.CommandId).Get<CommitReceipt>();
    }

    private static async Task<NativeTextCrashReceipt> CaptureReceiptAsync(string directory, ZoneTreeStore store,
        DatabaseEngine database, PartitionRef partition, string query, string[] expectedIds,
        string[] canonicalDocumentIds)
    {
        var documents = canonicalDocumentIds.Select(id =>
        {
            var reference = new EntityRef(partition, CrashFixtureValues.Orders, id);
            var value = store.Read(view => view.ReadOwnedValue(DocumentStorageKeys.RecordKey(reference)))!;
            return new NativeTextCrashDocument(id, value);
        }).ToArray();
        var principal = store.Read(view => view.ReadOwnedValue(KeySpace.Principal(CrashFixtureValues.Principal)))!;
        var resource = store.Read(view => view.ReadOwnedValue(KeySpace.Resource(partition.TenantId,
            partition.DatabaseId, CrashFixtureValues.Orders)))!;
        var credential = store.Read(view => view.ReadOwnedValue(KeySpace.ApiKey(CrashFixtureValues.Principal)))!;
        var canonicalState = NativeTextCanonicalStateCapture.Capture(store);
        var authorityFiles = await CaptureAuthorityFilesAsync(directory, CrashExecutionOptions.Child());
        var identity = store.Identity;
        var principalRecord = NativeSerialization.Deserialize<PrincipalRecord>(principal);
        var resourceRecord = NativeSerialization.Deserialize<ResourceDefinition>(resource);
        var scope = new TextProjectionScope(identity.NodeId, identity.Incarnation, identity.FormatVersion,
            identity.ReadGeneration, store.Position, partition, CrashFixtureValues.Orders, TextPath,
            principalRecord.Id, principalRecord.PolicyEpoch, resourceRecord.SchemaVersion);
        var manifestRecords = database.WithDocuments(CrashFixtureValues.Principal, partition,
            CrashFixtureValues.Orders, (_, _, visible) => CreateManifestRecords(visible), fieldUses: [TextPath]);
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            store.Position, partition, CrashFixtureValues.Orders, query, expectedIds, documents,
            principal, resource, authorityFiles, SHA256.HashData(credential), scope,
            manifestRecords, canonicalState);
    }

    private static NativeTextCrashManifestRecord[] CreateManifestRecords(DocumentRecord[] documents)
    {
        const int IndexInitialValue = 0;
        const int IndexSingleItemCount = 1;

        var records = new NativeTextCrashManifestRecord[documents.Length];
        for (var index = IndexInitialValue; index < documents.Length; index++)
        {
            records[index] = new((ulong)index + IndexSingleItemCount, documents[index].Reference, documents[index].Revision);
        }
        return records;
    }

    private static async Task<NativeTextCrashFile[]> CaptureAuthorityFilesAsync(string directory, Microsoft.Extensions.Options.IOptions<CrashHostExecutionOptions> executionOptions)
    {
        const string CaptureAuthorityFilesAsyncSearchPatternText = "*";

        var settings = executionOptions.Value;
        var files = new List<NativeTextCrashFile>();
        foreach (var path in Directory.EnumerateFiles(directory, CaptureAuthorityFilesAsyncSearchPatternText, SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            if (string.Equals(Path.GetFileName(path), OwnerLockName, StringComparison.Ordinal))
            {
                files.Add(new(OwnerLockName, SHA256.HashData(Array.Empty<byte>())));
                continue;
            }
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                settings.AuthorityReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            files.Add(new(Path.GetFileName(path), await SHA256.HashDataAsync(input)));
        }
        return [.. files];
    }
}

internal sealed class NativeTextCrashBoundary(NativeTextFaultStage expected)
{
    private const int GetObservedEmptyRead = 0;

    private int observed;
    internal bool Armed { get; set; }
    internal bool Observed => Volatile.Read(ref observed) != GetObservedEmptyRead;

    internal void Observe(NativeTextFaultStage stage)
    {
        const int ValueSingleItemCount = 1;
        const int EmptyExchange = 0;
        const string ObserveMessageText = "The requested native text stage was observed more than once.";

        if (!Armed || stage != expected)
        {
            return;
        }
        if (Interlocked.Exchange(ref observed, ValueSingleItemCount) != EmptyExchange)
        {
            throw new InvalidOperationException(ObserveMessageText);
        }
        CrashHostPause.AtBoundary();
    }
}
