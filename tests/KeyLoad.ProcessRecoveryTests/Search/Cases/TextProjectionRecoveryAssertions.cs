using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Authorization;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.ProcessRecoveryTests.StorageRecovery;
using KeyLoad.Query;
using KeyLoad.Server.Search;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.ProcessRecoveryTests.Search;

internal static class TextProjectionRecoveryAssertions
{
    internal static async Task RecoverAndVerifyAsync(string source, TextCrashReceipt receipt,
        CancellationToken cancellationToken)
    {
        await AssertAuthorityFilesUnchangedAsync(source, receipt, cancellationToken);
        var indexDirectory = Path.Combine(source, "search-indexes");
        using (var store = new ZoneTreeStore(new(source), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution()))
        {
            await AssertCanonicalCutAsync(store, receipt);
            var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.RuntimeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
            var request = new SearchRequest(receipt.Partition, receipt.Collection, "/text", receipt.Query, Limit: 10);
            var oracle = await new SearchEngine(database, RecoveryExecutionOptions.QueryExecution()).SearchAsync(CrashFixtureValues.Principal, request, cancellationToken);
            using var projection = new RuntimeTextProjection(indexDirectory, store.Identity.NodeId,
                RecoveryExecutionOptions.RuntimeText());
            var actual = await new SearchEngine(database, RecoveryExecutionOptions.QueryExecution(), projection).SearchAsync(CrashFixtureValues.Principal, request, cancellationToken);

            await AssertParityAsync(oracle, actual, receipt.ExpectedIds);
            await AssertCanonicalCutAsync(store, receipt);
            await AssertSingleCompletedGenerationAsync(indexDirectory, receipt, cancellationToken);
        }
        await AssertAuthorityFilesUnchangedAsync(source, receipt, cancellationToken);
    }

    private static async Task AssertCanonicalCutAsync(ZoneTreeStore store, TextCrashReceipt receipt)
    {
        await Assert.That(store.Identity.NodeId).IsEqualTo(receipt.NodeId);
        await Assert.That(store.Identity.Incarnation).IsEqualTo(receipt.Incarnation);
        await Assert.That(store.Identity.FormatVersion).IsEqualTo(receipt.DataEpoch);
        await Assert.That(store.Identity.ReadGeneration).IsEqualTo(receipt.ReadGeneration);
        await Assert.That(store.Position).IsEqualTo(receipt.Position);
        var canonicalState = TextCanonicalStateCapture.Capture(store);
        await Assert.That(canonicalState.RecordCount).IsEqualTo(receipt.CanonicalState.RecordCount);
        await Assert.That(canonicalState.Sha256).IsEquivalentTo(receipt.CanonicalState.Sha256,
            CollectionOrdering.Matching);
        foreach (var document in receipt.CanonicalDocuments)
        {
            var key = DocumentStorageKeys.RecordKey(receipt.Partition, receipt.Collection, document.Id);
            var current = store.Read(view => view.ReadOwnedValue(key));
            await Assert.That(current).IsEquivalentTo(document.Value, CollectionOrdering.Matching);
        }
        var principal = store.Read(view => view.ReadOwnedValue(KeySpace.Principal(CrashFixtureValues.Principal)));
        var resource = store.Read(view => view.ReadOwnedValue(KeySpace.Resource(receipt.Partition.TenantId,
            receipt.Partition.DatabaseId, receipt.Collection)));
        var credential = store.Read(view => view.ReadOwnedValue(KeySpace.ApiKey(CrashFixtureValues.Principal)));
        ArgumentNullException.ThrowIfNull(credential);
        await Assert.That(principal).IsEquivalentTo(receipt.PrincipalValue, CollectionOrdering.Matching);
        await Assert.That(resource).IsEquivalentTo(receipt.ResourceValue, CollectionOrdering.Matching);
        await Assert.That(SHA256.HashData(credential)).IsEquivalentTo(receipt.CredentialSha256,
            CollectionOrdering.Matching);
    }

    private static async Task AssertAuthorityFilesUnchangedAsync(string source, TextCrashReceipt receipt,
        CancellationToken cancellationToken)
    {
        var expected = receipt.AuthorityFiles.ToDictionary(file => file.Name,
            file => Convert.ToHexString(file.Sha256), StringComparer.Ordinal);
        await RecoveryFileInventory.AssertAuthorityUnchangedAsync(source, expected, cancellationToken);
    }

    private static async Task AssertParityAsync(RankedDocument[] expected, RankedDocument[] actual,
        string[] expectedIds)
    {
        await Assert.That(actual.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(expected.Select(result => result.Document.Reference.Id).ToArray(), CollectionOrdering.Matching);
        await Assert.That(actual.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(expectedIds, CollectionOrdering.Matching);
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Document).IsEquivalentTo(expected[index].Document);
            await Assert.That(actual[index].Score).IsEqualTo(expected[index].Score);
        }
    }

    private static async Task AssertSingleCompletedGenerationAsync(string indexDirectory,
        TextCrashReceipt receipt, CancellationToken cancellationToken)
    {
        var generations = Directory.EnumerateDirectories(indexDirectory)
            .Where(path => Path.GetFileName(path).StartsWith(TextProtocol.GenerationPrefix,
                StringComparison.Ordinal)).ToArray();
        await Assert.That(generations.Length).IsEqualTo(1);
        var manifestPath = Path.Combine(generations[0], TextProtocol.ManifestFile);
        var manifest = TextEnvelopeCodec.Decode<TextManifest>(
            await File.ReadAllBytesAsync(manifestPath, cancellationToken));
        await Assert.That(manifest.Scope.NodeId).IsEqualTo(receipt.NodeId);
        await Assert.That(manifest.Scope.Incarnation).IsEqualTo(receipt.Incarnation);
        await Assert.That(manifest.Scope.DataEpoch).IsEqualTo(receipt.DataEpoch);
        await Assert.That(manifest.Scope.ReadGeneration).IsEqualTo(receipt.ReadGeneration);
        await Assert.That(manifest.Scope.Position).IsEqualTo(receipt.Position);
        await Assert.That(manifest.Scope).IsEquivalentTo(receipt.Scope);
        var records = manifest.Records.Select(record =>
            new TextCrashManifestRecord(record.Id, record.Reference, record.Revision)).ToArray();
        await Assert.That(records).IsEquivalentTo(receipt.ManifestRecords, CollectionOrdering.Matching);
    }
}
