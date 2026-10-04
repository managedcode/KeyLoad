using KeyLoad.Storage.IO;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OfflineRegularFileIdentityTests
{
    private const string RootPrefix = "keyload-offline-file-identity-";

    [Test]
    public async Task AcEpoch012ReadsOnlyTheInspectedRegularFile()
    {
        await WithDirectoryAsync(async root =>
        {
            var path = Path.Combine(root, "input.bin");
            byte[] expected = [0x00, 0x71, 0xFF, 0x18];
            await File.WriteAllBytesAsync(path, expected);
            var identity = OfflineRegularFile.Inspect(path);
            await using var stream = OfflineRegularFile.OpenWithIdentity(path, identity,
                FileAccess.Read, FileShare.Read, 4096);
            var actual = new byte[expected.Length];
            var count = await stream.ReadAsync(actual);

            await Assert.That(count).IsEqualTo(expected.Length);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
            await Assert.That(stream.ReadByte()).IsEqualTo(-1);
        });
    }

    [Test]
    public async Task AcEpoch012RejectsARegularPathReplacedAfterInspection()
    {
        await WithDirectoryAsync(async root =>
        {
            var path = Path.Combine(root, "input.bin");
            var retained = Path.Combine(root, "retained.bin");
            var replacement = Path.Combine(root, "replacement.bin");
            byte[] originalBytes = [0x14, 0x29, 0x73];
            byte[] replacementBytes = [0xA1, 0xB2, 0xC3];
            await File.WriteAllBytesAsync(path, originalBytes);
            var identity = OfflineRegularFile.Inspect(path);
            await File.WriteAllBytesAsync(replacement, replacementBytes);
            File.Move(path, retained);
            File.Move(replacement, path);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => OfflineRegularFile.OpenWithIdentity(
                path, identity, FileAccess.Read, FileShare.Read, 4096));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
            await AssertBytesAsync(retained, originalBytes);
            await AssertBytesAsync(path, replacementBytes);
        });
    }

    private static async Task AssertBytesAsync(string path, byte[] expected)
    {
        var actual = await File.ReadAllBytesAsync(path);
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
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
