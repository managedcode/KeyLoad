using System.Net.Sockets;
using KeyLoad.Storage.IO;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OfflineRegularFileEntryTypeTests
{
    private const string RootPrefix = "keyload-offline-file-type-";

    [Test]
    public async Task AcEpoch012RejectsDirectorySymlinkSocketAndDeviceWithoutChangingTargets()
    {
        await WithDirectoryAsync(async root =>
        {
            var target = Path.Combine(root, "target.bin");
            var link = Path.Combine(root, "target.link");
            var directory = Path.Combine(root, "directory");
            var socketPath = Path.Combine(root, "local.socket");
            byte[] targetBytes = [0x10, 0x00, 0x90];
            await File.WriteAllBytesAsync(target, targetBytes);
            File.CreateSymbolicLink(link, target);
            Directory.CreateDirectory(directory);
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(socketPath));

            await AssertUnsupportedAsync(link);
            await AssertUnsupportedAsync(directory);
            await AssertUnsupportedAsync(socketPath);
            await AssertUnsupportedAsync("/dev/null");
            var actual = await File.ReadAllBytesAsync(target);
            await Assert.That(actual.SequenceEqual(targetBytes)).IsTrue();
        });
    }

    private static async Task AssertUnsupportedAsync(string path)
    {
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => OfflineRegularFile.Inspect(path));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.FormatUnsupported);
    }

    private static async Task WithDirectoryAsync(Func<string, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        { await action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
