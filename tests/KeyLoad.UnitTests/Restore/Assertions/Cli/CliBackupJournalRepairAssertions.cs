using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CliBackupJournalRepairAssertions
{
    private const string ErrorCodeKey = "errorCode";
    private const string DetailKey = "detail";
    private const string NodeIdKey = "nodeId";
    private const string IncarnationKey = "incarnation";
    private const string DispatchPausedKey = "dispatchPaused";
    internal static async Task RefusedAsync(CliBackupRestoreProcessResult result)
    {
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEmpty();
        using var problem = JsonDocument.Parse(result.StandardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeKey).GetString()).IsEqualTo("Corruption");
        await Assert.That(problem.RootElement.GetProperty(DetailKey).GetString())
            .IsEqualTo(ZoneTreePersistenceFormat.JournalChecksumInvalid);
    }

    internal static async Task ReceiptAsync(CliBackupRestoreProcessResult result, StoreIdentity identity)
    {
        using var receipt = JsonDocument.Parse(result.StandardOutput);
        await Assert.That(receipt.RootElement.GetProperty(NodeIdKey).GetGuid()).IsEqualTo(identity.NodeId);
        await Assert.That(receipt.RootElement.GetProperty(IncarnationKey).GetGuid()).IsEqualTo(identity.Incarnation);
        await Assert.That(receipt.RootElement.GetProperty(DispatchPausedKey).GetBoolean()).IsTrue();
        await Assert.That(identity.DispatchPaused).IsTrue();
    }
}
