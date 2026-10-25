using KeyLoad.FunctionalTests.OrleansExecution;
using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.FunctionalTests.StorageRecovery;

/// <summary>Current node-owner special-file rejection remains a real execution filesystem flow.</summary>
internal sealed class PartitionRootAdmissionSpecialEntryTests
{
    private const string OwnerLockName = "node.owner.lock";
    private const string UnsupportedLayoutDetail = "The physical node directory is not a supported current layout.";

    [Test]
    public async Task FifoOwnerLockRejectsWithoutMutationThenCurrentWriteReopens()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            await PartitionRootAdmissionFixture.RunAsync(VerifyUnixFifoAsync).ConfigureAwait(false);
        }
        else
        {
            TUnit.Core.Skip.Test("Execution FIFO admission requires a Linux or macOS test host.");
        }
    }

    private static async Task VerifyUnixFifoAsync(PartitionRootAdmissionFixture fixture)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            fixture.CreateExistingRoot();
            var path = Path.Combine(fixture.Root, OwnerLockName);
            RequestCqrsProbeFifo.Create(path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var entries = Directory.GetFileSystemEntries(fixture.Root);
            var mode = File.GetUnixFileMode(path);
            var rejection = await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.OpenAndDisposeHostAsync);
            await Assert.That(rejection!.Code).IsEqualTo(ErrorCode.FormatUnsupported);
            await Assert.That(rejection.Message).IsEqualTo(UnsupportedLayoutDetail);
            await Assert.That(Directory.GetFileSystemEntries(fixture.Root)).IsEquivalentTo(entries);
            await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(mode);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OfflineRegularFile.Inspect(path)).Code)
                .IsEqualTo(ErrorCode.FormatUnsupported);
            File.Delete(path);
            await fixture.AssertHealthyWriteReopenAsync();
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
    }
}
