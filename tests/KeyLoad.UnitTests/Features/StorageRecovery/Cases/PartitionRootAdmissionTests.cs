using KeyLoad.Storage.IO;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>REQ/AC-NATIVE-007: reject foreign node-root state before owner or store mutation.</summary>
internal sealed class PartitionRootAdmissionTests
{
    private const string UnknownEntryName = "unowned";
    private const string CanonicalDirectoryName = "database";
    private const string OwnerLockName = "node.owner.lock";
    private const string UnsupportedLayoutDetail = "The physical node directory is not a supported current layout.";

    [Test]
    public async Task UnknownFileRejectsUnchangedThenCurrentWriteReopens()
        => await AssertRejectedThenHealthyAsync(UnknownEntryName, CreateFile, VerifyFileAsync, File.Delete);

    [Test]
    public async Task UnknownDirectoryRejectsUnchangedThenCurrentWriteReopens()
        => await AssertRejectedThenHealthyAsync(UnknownEntryName, CreateDirectory, VerifyDirectoryAsync,
            path => Directory.Delete(path, recursive: true));

    [Test]
    public async Task UnownedLinkRejectsUnchangedThenCurrentWriteReopens()
        => await AssertRejectedThenHealthyAsync(UnknownEntryName, CreateLink, VerifyLinkAsync, File.Delete);

    private static async Task AssertRejectedThenHealthyAsync(string entryName, Action<string, string> create,
        Func<string, string, Task> verify, Action<string> remove)
    {
        using var fixture = new PartitionRootAdmissionFixture();
        fixture.CreateExistingRoot();
        var path = Path.Combine(fixture.Root, entryName);
        var target = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "current-layout-target");
        create(path, target);
        var entries = Directory.GetFileSystemEntries(fixture.Root);
        UnixFileMode? mode = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? File.GetUnixFileMode(fixture.Root)
            : null;
        var rejection = await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.OpenAndDisposeHostAsync);
        await Assert.That(rejection!.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(rejection.Message).IsEqualTo(UnsupportedLayoutDetail);
        await Assert.That(Directory.GetFileSystemEntries(fixture.Root)).IsEquivalentTo(entries);
        if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && mode is { } originalMode)
        {
            await Assert.That(File.GetUnixFileMode(fixture.Root)).IsEqualTo(originalMode);
        }
        await verify(path, target);
        remove(path);
        await fixture.AssertHealthyWriteReopenAsync();
    }

    [Test]
    public async Task WrongKnownEntryKindsRejectBeforeMutation()
    {
        await AssertRejectedThenHealthyAsync(CanonicalDirectoryName, CreateFile, VerifyFileAsync, File.Delete);
        await AssertRejectedThenHealthyAsync(OwnerLockName, CreateDirectory, VerifyDirectoryAsync,
            path => Directory.Delete(path, recursive: true));
        await AssertRejectedThenHealthyAsync(KeyLoad.Server.Features.Search.NativeTextOnlineRoot.DirectoryName,
            CreateFile, VerifyFileAsync, File.Delete);
        await AssertRejectedThenHealthyAsync(KeyLoad.Server.Features.Search.NativeTextOnlineRoot.DirectoryName,
            CreateLink, VerifyLinkAsync, File.Delete);
        await AssertRejectedThenHealthyAsync(KeyLoad.Server.ClusterBackupOwnerArchive.DirectoryName,
            CreateFile, VerifyFileAsync, File.Delete);
        await AssertRejectedThenHealthyAsync(KeyLoad.Server.ClusterBackupOwnerArchive.DirectoryName,
            CreateLink, VerifyLinkAsync, File.Delete);
    }

    [Test]
    public async Task NonDirectoryNodeRootRejectsUnchangedThenFreshRootReopens()
    {
        using var fixture = new PartitionRootAdmissionFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Root)!);
        await File.WriteAllBytesAsync(fixture.Root, [0x31, 0x00, 0x32]);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(fixture.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        UnixFileMode? mode = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? File.GetUnixFileMode(fixture.Root)
            : null;
        var rejection = await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.OpenAndDisposeHostAsync);
        await Assert.That(rejection!.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(rejection.Message).IsEqualTo(UnsupportedLayoutDetail);
        await Assert.That(await File.ReadAllBytesAsync(fixture.Root)).IsEquivalentTo(new byte[] { 0x31, 0x00, 0x32 });
        if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && mode is { } originalMode)
        {
            await Assert.That(File.GetUnixFileMode(fixture.Root)).IsEqualTo(originalMode);
        }
        File.Delete(fixture.Root);
        await fixture.AssertHealthyWriteReopenAsync();
    }

    [Test]
    public async Task RootLinkRejectsWithoutTouchingTargetThenFreshRootReopens()
    {
        using var fixture = new PartitionRootAdmissionFixture();
        var parent = Path.GetDirectoryName(fixture.Root)!;
        Directory.CreateDirectory(parent);
        var target = Path.Combine(parent, "foreign-node-root");
        Directory.CreateDirectory(target);
        var targetFile = Path.Combine(target, "sentinel");
        await File.WriteAllBytesAsync(targetFile, [0x71, 0x00, 0x72]);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(targetFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        Directory.CreateSymbolicLink(fixture.Root, target);
        var rejection = await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.OpenAndDisposeHostAsync);
        await Assert.That(rejection!.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(rejection.Message).IsEqualTo(UnsupportedLayoutDetail);
        await Assert.That(new DirectoryInfo(fixture.Root).LinkTarget).IsNotNull();
        await Assert.That(await File.ReadAllBytesAsync(targetFile)).IsEquivalentTo(new byte[] { 0x71, 0x00, 0x72 });
        await Assert.That(Directory.GetFileSystemEntries(target)).IsEquivalentTo([targetFile]);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            await Assert.That(File.GetUnixFileMode(target)).IsEqualTo(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await Assert.That(File.GetUnixFileMode(targetFile)).IsEqualTo(
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        Directory.Delete(fixture.Root);
        await fixture.AssertHealthyWriteReopenAsync();
    }

    [Test]
    public async Task ExistingRegularOwnerLockStillExcludesSecondHost()
    {
        using var fixture = new PartitionRootAdmissionFixture();
        fixture.CreateExistingRoot();
        var ownerPath = Path.Combine(fixture.Root, OwnerLockName);
        {
            using var owner = new FileStream(ownerPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            UnixFileMode? ownerMode = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? File.GetUnixFileMode(ownerPath)
                : null;
            await Assert.ThrowsExactlyAsync<IOException>(fixture.OpenAndDisposeHostAsync);
            await Assert.That(Directory.GetFileSystemEntries(fixture.Root)).IsEquivalentTo([ownerPath]);
            await Assert.That(new FileInfo(ownerPath).Length).IsEqualTo(0L);
            if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && ownerMode is { } originalMode)
            {
                await Assert.That(File.GetUnixFileMode(ownerPath)).IsEqualTo(originalMode);
            }
        }
        await fixture.AssertHealthyWriteReopenAsync();
    }

    private static void CreateFile(string path, string target)
    {
        _ = target;
        File.WriteAllBytes(path, [0x41, 0x00, 0x42]);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void CreateDirectory(string path, string target)
    {
        _ = target;
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "sentinel"), [0x51, 0x00, 0x52]);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(Path.Combine(path, "sentinel"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void CreateLink(string path, string target)
    {
        File.WriteAllBytes(target, [0x61, 0x00, 0x62]);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.CreateSymbolicLink(path, target);
    }

    private static async Task VerifyFileAsync(string path, string target)
    {
        _ = target;
        await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(new byte[] { 0x41, 0x00, 0x42 });
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static async Task VerifyDirectoryAsync(string path, string target)
    {
        _ = target;
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(path, "sentinel")))
            .IsEquivalentTo(new byte[] { 0x51, 0x00, 0x52 });
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await Assert.That(File.GetUnixFileMode(Path.Combine(path, "sentinel")))
                .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static async Task VerifyLinkAsync(string path, string target)
    {
        await Assert.That(new FileInfo(path).LinkTarget).IsNotNull();
        await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(new byte[] { 0x61, 0x00, 0x62 });
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            await Assert.That(File.GetUnixFileMode(target)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

}

/// <summary>Current node-owner special-file rejection remains a real native filesystem flow.</summary>
internal sealed class PartitionRootAdmissionSpecialEntryTests
{
    private const string OwnerLockName = "node.owner.lock";
    private const string UnsupportedLayoutDetail = "The physical node directory is not a supported current layout.";

    [Test]
    public async Task FifoOwnerLockRejectsWithoutMutationThenCurrentWriteReopens()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            using var fixture = new PartitionRootAdmissionFixture();
            fixture.CreateExistingRoot();
            var path = Path.Combine(fixture.Root, OwnerLockName);
            RequestCqrsProbeNativeFifo.Create(path);
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
            TUnit.Core.Skip.Test("Native FIFO admission requires a Linux or macOS test host.");
        }
    }
}
