using KeyLoad.Core;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class GenuineStoredOutcomeFrameTestSupport
{
    private const string TrialPrefix = "keyload-genuine-prior-outcome-";
    private const int TrialTimeoutSeconds = 75;
    private const int CleanupTimeoutSeconds = 30;
    private const string CleanupFailureKey = "KeyLoad.GenuinePriorOutcomeCleanupFailure";
    private const string MissingFrameReceipt = "The verified native6 probe omitted its bounded outcome frame.";

    internal static async Task RunAsync(Func<string, CancellationToken, Task> test, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TrialTimeoutSeconds));
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            await test(root, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            await CleanupAsync(root, activeFailure);
        }
    }

    internal static async Task AssertPriorFrameAsync(string root, CancellationToken cancellationToken)
    {
        var commandId = Guid.NewGuid();
        var source = Path.Combine(root, "native6-source");
        var target = Path.Combine(root, "current-reader");
        var receipt = await EpochPriorExecutableFixture.CreateOutcomeFrameAsync(source, commandId, cancellationToken);
        var bytes = FrameBytes(receipt);
        var prior = NativeSerialization.Deserialize<StoredOutcome>(bytes);
        await GenuineStoredOutcomeFrameAssertions.AssertPriorDefaultsAsync(prior, receipt);
        var principalId = receipt.OutcomePrincipalId
            ?? throw new InvalidDataException(MissingFrameReceipt);
        await WithOwnedStoreAsync(target, receipt.Incarnation, async store =>
        {
            store.Commit((transaction, _) =>
            {
                transaction.Put(KeySpace.LegacyOutcomeKey(principalId, commandId), bytes);
                return true;
            });
            await GenuineStoredOutcomeFrameAssertions.AssertCurrentReadAsync(
                store, prior, bytes, principalId, commandId);
        });
    }

    internal static Task AssertMissingCommandRejectedAsync(string root, CancellationToken cancellationToken)
        => EpochPriorExecutableFixture.AssertMissingOutcomeCommandRejectedAsync(
            Path.Combine(root, "native6-missing-command"), cancellationToken);

    internal static async Task AssertTruncatedFrameAsync(string root, CancellationToken cancellationToken)
    {
        var commandId = Guid.NewGuid();
        var receipt = await EpochPriorExecutableFixture.CreateOutcomeFrameAsync(
            Path.Combine(root, "native6-source"), commandId, cancellationToken);
        var bytes = FrameBytes(receipt);
        var principalId = receipt.OutcomePrincipalId
            ?? throw new InvalidDataException(MissingFrameReceipt);
        await WithOwnedStoreAsync(Path.Combine(root, "current-truncated"), receipt.Incarnation, async store =>
        {
            store.Commit((transaction, _) =>
            {
                transaction.Put(KeySpace.LegacyOutcomeKey(principalId, commandId), bytes[..^1]);
                return true;
            });
            var database = CreateAuthorizedDatabase(store, principalId);
            var position = store.Position;
            var result = database.ResolveOutcome(
                CreatePriorConfigureOperation(store, principalId, commandId));
            await Assert.That(result.Error).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(store.Position).IsEqualTo(position);
            await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.GlobalOutcome(principalId, commandId))))
                .IsNull();
            await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.UnknownOutcome(principalId, commandId))))
                .IsNull();
            var retained = store.Read(view => view.ReadOwnedValue(KeySpace.LegacyOutcomeKey(principalId, commandId)))
                ?? throw new InvalidOperationException("The malformed outcome disappeared during its read.");
            await Assert.That(retained.AsSpan().SequenceEqual(bytes.AsSpan(0, bytes.Length - 1))).IsTrue();
        });
    }

    private static DatabaseEngine CreateAuthorizedDatabase(ZoneTreeStore store, string principalId)
    {
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource());
        database.Bootstrap(new(principalId, "system", [new("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true }, DatabaseEngine.Credential("outcome-probe-key", principalId,
            "epoch-outcome-probe-owned-admin-key-2026"));
        return database;
    }

    private static ReplicatedOperation CreatePriorConfigureOperation(ZoneTreeStore store, string principalId, Guid commandId)
    {
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource());
        var request = new ConfigureResourceRequest("epoch-outcome-tenant", "epoch-outcome-database",
            new ResourceDefinition("epoch-outcome-resource", ResourceKind.Collection, "epoch-outcome-domain"));
        return database.CreateNativeOperation(OperationKind.ConfigureResource, commandId, principalId,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
    }

    private static async Task WithOwnedStoreAsync(string path, Guid incarnation,
        Func<ZoneTreeStore, Task> work)
    {
        ZoneTreeStore? store = null;
        Exception? primary = null;
        try
        {
            try
            {
                store = new ZoneTreeStore(new(path) { Incarnation = incarnation }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
                await work(store);
            }
            catch (Exception failure)
            {
                primary = failure;
                throw;
            }
            finally { store?.Dispose(); }
        }
        catch (Exception cleanupFailure) when (primary is not null && !ReferenceEquals(primary, cleanupFailure))
        {
            throw new AggregateException("The native outcome trial and its store cleanup failed.", primary, cleanupFailure);
        }
    }

    private static byte[] FrameBytes(EpochPriorProbeReceipt receipt)
        => Convert.FromBase64String(receipt.OutcomeFrameBase64
            ?? throw new InvalidDataException(MissingFrameReceipt));

    private static async Task CleanupAsync(string root, Exception? activeFailure)
    {
        var source = Path.Combine(root, "native6-source");
        var target = Path.Combine(root, "current-reader");
        var truncated = Path.Combine(root, "current-truncated");
        var missingCommand = Path.Combine(root, "native6-missing-command");
        try
        {
            AssertHandlesReleased(source, target, truncated, missingCommand);
            if (activeFailure is null && Directory.Exists(root))
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
                await StoragePublicationRecoveryTests.DeleteTrialAsync(root, cleanup.Token);
            }
        }
        catch (Exception cleanupFailure)
        {
            if (activeFailure is null)
            { throw; }
            activeFailure.Data[CleanupFailureKey] = cleanupFailure;
        }
    }

    private static void AssertHandlesReleased(params string[] stores)
    {
        foreach (var store in stores)
        {
            if (Directory.Exists(store))
            { EpochUpgradeFileInventory.AssertNativeHandlesReleased(store); }
        }
    }
}
