using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Search;
using KeyLoad.Query;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Security;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Search;

internal sealed class NativeTextProjectionProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-native-text-process-";
    private const string StoreDirectoryName = "canonical";
    private const string ReceiptFileName = "canonical-cut.bin";
    private const int TimeoutSeconds = 45;
    private const int CleanupSeconds = 30;

    [Test]
    [Arguments(NativeTextFaultStage.OwnerFlushed, false)]
    [Arguments(NativeTextFaultStage.NativePostingWritten, false)]
    [Arguments(NativeTextFaultStage.NativeInventoryFlushed, false)]
    [Arguments(NativeTextFaultStage.ManifestPublished, false)]
    [Arguments(NativeTextFaultStage.GenerationActivated, false)]
    [Arguments(NativeTextFaultStage.OwnerFlushed, true)]
    [Arguments(NativeTextFaultStage.NativePostingWritten, true)]
    [Arguments(NativeTextFaultStage.NativeInventoryFlushed, true)]
    [Arguments(NativeTextFaultStage.ManifestPublished, true)]
    [Arguments(NativeTextFaultStage.GenerationActivated, true)]
    public async Task AcFts003004005NativeProcessCutsPreserveCanonicalCutAndRebuildSearch(
        NativeTextFaultStage stage, bool replacement)
        => await RunTrialAsync(stage, replacement, TestContext.Current!.Execution.CancellationToken);

    private static async Task RunTrialAsync(NativeTextFaultStage stage, bool replacement,
        CancellationToken callerToken)
    {
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, StoreDirectoryName);
        var receiptPath = Path.Combine(root, ReceiptFileName);
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        Process? process = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            process = StartCrashProcess(source, receiptPath, stage, replacement);
            await NativeTextCrashMarkerReader.AwaitAsync(process.StandardOutput, process.StandardError,
                timeout.Token);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            await JoinOutputReadersAsync(process, timeout.Token);
            await KilledProcessFileReadiness.WaitAsync(source, timeout.Token);
            EpochUpgradeFileInventory.AssertNativeHandlesReleased(source);
            var receipt = NativeSerialization.Deserialize<NativeTextCrashReceipt>(
                await File.ReadAllBytesAsync(receiptPath, timeout.Token));
            await NativeTextProjectionRecoveryAssertions.RecoverAndVerifyAsync(source, receipt, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds));
            await EpochUpgradeCleanup.SettleAsync(process, root, source, activeFailure, cleanup.Token);
        }
    }

    private static Process StartCrashProcess(string source, string receiptPath, NativeTextFaultStage stage,
        bool replacement)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            typeof(CrashHostMarker).Assembly.Location,
            source,
            receiptPath,
            stage.ToString(),
            replacement.ToString(),
            NativeTextCrashScenario.Mode
        })
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start native text CrashHost.");
    }

    private static async Task JoinOutputReadersAsync(Process process, CancellationToken cancellationToken)
    {
        await NativeTextCrashMarkerReader.DrainAsync(process.StandardOutput, cancellationToken);
        await NativeTextCrashMarkerReader.DrainAsync(process.StandardError, cancellationToken);
    }
}

internal static class NativeTextCrashMarkerReader
{
    private const int OutputByteLimit = 8_192;
    private const int ErrorCharacterLimit = 1_024;
    private const int LineLimit = 32;
    private const int BufferSize = 256;

    internal static async Task AwaitAsync(StreamReader output, StreamReader error, CancellationToken cancellationToken)
    {
        var buffer = new char[BufferSize];
        var line = new StringBuilder();
        var outputBytes = 0;
        var lineCount = 0;
        while (true)
        {
            var read = await output.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                var diagnostic = await ReadErrorSummaryAsync(error, cancellationToken);
                throw new InvalidOperationException("The native text process exited before its requested stage marker: " + diagnostic);
            }
            outputBytes += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, read));
            if (outputBytes > OutputByteLimit)
            {
                throw new InvalidOperationException("Native text process output exceeded the marker wait bound.");
            }
            if (ContainsMarker(buffer.AsSpan(0, read), line, ref lineCount))
            {
                return;
            }
        }
    }

    internal static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[BufferSize];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) != 0)
        {
        }
    }

    private static bool ContainsMarker(ReadOnlySpan<char> chunk, StringBuilder line, ref int lineCount)
    {
        foreach (var character in chunk)
        {
            if (character != '\n')
            {
                line.Append(character);
                continue;
            }
            if (line.Length > 0 && line[^1] == '\r')
            {
                line.Length--;
            }
            var matches = string.Equals(line.ToString(), CrashFixtureValues.CrashMarker, StringComparison.Ordinal);
            line.Clear();
            if (matches)
            {
                return true;
            }
            if (++lineCount > LineLimit)
            {
                throw new InvalidOperationException("Native text process output exceeded the marker line bound.");
            }
        }
        return false;
    }

    private static async Task<string> ReadErrorSummaryAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[BufferSize];
        var summary = new StringBuilder();
        while (summary.Length < ErrorCharacterLimit)
        {
            var count = Math.Min(buffer.Length, ErrorCharacterLimit - summary.Length);
            var read = await reader.ReadAsync(buffer.AsMemory(0, count), cancellationToken);
            if (read == 0)
            {
                break;
            }
            summary.Append(buffer, 0, read);
        }
        return summary.ToString();
    }
}

internal static class NativeTextProjectionRecoveryAssertions
{
    internal static async Task RecoverAndVerifyAsync(string source, NativeTextCrashReceipt receipt,
        CancellationToken cancellationToken)
    {
        await AssertAuthorityFilesUnchangedAsync(source, receipt, cancellationToken);
        var indexDirectory = Path.Combine(source, "search-indexes");
        using (var store = new ZoneTreeStore(new(source), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution()))
        {
            await AssertCanonicalCutAsync(store, receipt);
            var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
            var request = new SearchRequest(receipt.Partition, receipt.Collection, "/text", receipt.Query, Limit: 10);
            var oracle = await new SearchEngine(database, RecoveryExecutionOptions.QueryExecution()).SearchAsync(CrashFixtureValues.Principal, request, cancellationToken);
            using var projection = new NativeTextProjection(indexDirectory, RecoveryExecutionOptions.DatabaseLimits(database.Limits), store.Identity.NodeId,
                RecoveryExecutionOptions.NativeText());
            var actual = await new SearchEngine(database, RecoveryExecutionOptions.QueryExecution(), projection).SearchAsync(CrashFixtureValues.Principal, request, cancellationToken);

            await AssertParityAsync(oracle, actual, receipt.ExpectedIds);
            await AssertCanonicalCutAsync(store, receipt);
            await AssertSingleCompletedGenerationAsync(indexDirectory, receipt, cancellationToken);
        }
        await AssertAuthorityFilesUnchangedAsync(source, receipt, cancellationToken);
    }

    private static async Task AssertCanonicalCutAsync(ZoneTreeStore store, NativeTextCrashReceipt receipt)
    {
        await Assert.That(store.Identity.NodeId).IsEqualTo(receipt.NodeId);
        await Assert.That(store.Identity.Incarnation).IsEqualTo(receipt.Incarnation);
        await Assert.That(store.Identity.FormatVersion).IsEqualTo(receipt.DataEpoch);
        await Assert.That(store.Identity.ReadGeneration).IsEqualTo(receipt.ReadGeneration);
        await Assert.That(store.Position).IsEqualTo(receipt.Position);
        var canonicalState = NativeTextCanonicalStateCapture.Capture(store);
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

    private static async Task AssertAuthorityFilesUnchangedAsync(string source, NativeTextCrashReceipt receipt,
        CancellationToken cancellationToken)
    {
        var expected = receipt.AuthorityFiles.ToDictionary(file => file.Name,
            file => Convert.ToHexString(file.Sha256), StringComparer.Ordinal);
        await EpochUpgradeFileInventory.AssertAuthorityUnchangedAsync(source, expected, cancellationToken);
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
        NativeTextCrashReceipt receipt, CancellationToken cancellationToken)
    {
        var generations = Directory.EnumerateDirectories(indexDirectory)
            .Where(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal)).ToArray();
        await Assert.That(generations.Length).IsEqualTo(1);
        var manifestPath = Path.Combine(generations[0], NativeTextProtocol.ManifestFile);
        var manifest = NativeTextEnvelopeCodec.Decode<NativeTextManifest>(
            await File.ReadAllBytesAsync(manifestPath, cancellationToken));
        await Assert.That(manifest.Scope.NodeId).IsEqualTo(receipt.NodeId);
        await Assert.That(manifest.Scope.Incarnation).IsEqualTo(receipt.Incarnation);
        await Assert.That(manifest.Scope.DataEpoch).IsEqualTo(receipt.DataEpoch);
        await Assert.That(manifest.Scope.ReadGeneration).IsEqualTo(receipt.ReadGeneration);
        await Assert.That(manifest.Scope.Position).IsEqualTo(receipt.Position);
        await Assert.That(manifest.Scope).IsEquivalentTo(receipt.Scope);
        var records = manifest.Records.Select(record =>
            new NativeTextCrashManifestRecord(record.Id, record.Reference, record.Revision)).ToArray();
        await Assert.That(records).IsEquivalentTo(receipt.ManifestRecords, CollectionOrdering.Matching);
    }
}
