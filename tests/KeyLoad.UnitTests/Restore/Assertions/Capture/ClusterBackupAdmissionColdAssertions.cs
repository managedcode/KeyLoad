using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class ClusterBackupAdmissionColdAssertions
{
    internal static async Task RequireAsync(PartitionHostRecoveryFixture files, PartitionHost cold,
        byte[] originalImage, string principal)
    {
        var limits = UnitExecutionOptions.DatabaseLimits();
        var actual = cold.Database.Store.Read(view =>
        {
            var page = view.Scan([], limits.Value.MaxScanRecords);
            if (page.HasMore)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, "The cold native image exceeded its actual scan bound.");
            }
            return NativeSerialization.Serialize(page.Records);
        });
        await Assert.That(actual.AsSpan().SequenceEqual(originalImage)).IsTrue();
        await Assert.That(cold.Database.Authenticate(files.Options.AdminKey, TimeProvider.System.GetUtcNow()))
            .IsEqualTo(principal);
    }
}
