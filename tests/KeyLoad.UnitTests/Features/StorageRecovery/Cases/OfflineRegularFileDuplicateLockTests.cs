using KeyLoad.Storage.IO;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OfflineRegularFileDuplicateLockTests
{
    private const string RootPrefix = "keyload-offline-duplicate-lock-";

    [Test]
    [Arguments(FileShare.None, false)]
    [Arguments(FileShare.None, true)]
    [Arguments(FileShare.Read, false)]
    [Arguments(FileShare.Read, true)]
    public async Task AcEpoch012FinishedOwnerUnlocksWhileNativeDuplicateRemainsAlive(FileShare share, bool useAsync)
    {
        await WithFileAsync(async path =>
        {
            byte[] expected = [0xA1, 0xB2, 0xC3];
            var bufferBytes = UnitExecutionOptions.StorageExecution().Value.StreamBufferBytes;
            using var owner = OfflineRegularFile.Open(path, FileAccess.ReadWrite, share, bufferBytes);
            var originalHandle = owner.SafeFileHandle;
            using var duplicate = OfflineNativeDescriptorDuplicate.Create(originalHandle);
            AssertLiveOwnerExcludesIndependentOpen(path, bufferBytes);
            await owner.WriteAsync(expected, TestContext.Current!.Execution.CancellationToken);

            await DisposeAndJoinAsync(owner, useAsync);

            await Assert.That(originalHandle.IsClosed).IsTrue();
            await Assert.That(duplicate.IsClosed).IsFalse();
            await Assert.That(owner.CanRead).IsFalse();
            await using var reopened = OfflineRegularFile.Open(path, FileAccess.Read, FileShare.None, bufferBytes);
            var actual = new byte[expected.Length];
            await reopened.ReadExactlyAsync(actual, TestContext.Current!.Execution.CancellationToken);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
            await Assert.That(reopened.ReadByte()).IsEqualTo(-1);
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcEpoch012EarlyOriginalHandleClosureInvalidatesPendingBorrowedIo(bool useAsync)
    {
        await WithFileAsync(async path =>
        {
            byte[] expected = [0x31, 0x42, 0x53];
            var bufferBytes = UnitExecutionOptions.StorageExecution().Value.StreamBufferBytes;
            using var owner = OfflineRegularFile.Open(path, FileAccess.ReadWrite, FileShare.None, bufferBytes);
            var originalHandle = owner.SafeFileHandle;
            owner.WriteByte(0xFF);
            originalHandle.Dispose();

            await DisposeAndJoinAsync(owner, useAsync);

            await Assert.That(originalHandle.IsClosed).IsTrue();
            await Assert.That(owner.CanRead).IsFalse();
            await Assert.That(owner.CanWrite).IsFalse();
            var actual = await File.ReadAllBytesAsync(path, TestContext.Current!.Execution.CancellationToken);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        });
    }

    [Test]
    [Arguments(FileShare.None)]
    [Arguments(FileShare.Read)]
    public async Task AcEpoch012FailedStreamTransferUnlocksBeforeClosingTheNativeLease(FileShare share)
    {
        await WithFileAsync(async path =>
        {
            byte[] expected = [0x31, 0x42, 0x53];
            var bufferBytes = UnitExecutionOptions.StorageExecution().Value.StreamBufferBytes;
            using var original = OfflineRegularFile.Open(path, FileAccess.ReadWrite, share, bufferBytes);
            using var leaseHandle = OfflineNativeDescriptorDuplicate.Create(original.SafeFileHandle);
            DisposeSynchronous(original);
            using var retainedDuplicate = OfflineNativeDescriptorDuplicate.Create(leaseHandle);
            using (var lease = new OfflineNativeHandleLease(leaseHandle))
            {
                lease.AcquireLock(share);
                AssertLiveOwnerExcludesIndependentOpen(path, bufferBytes);
                var failure = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                {
                    using var unexpected = lease.TransferToStream((FileAccess)0, bufferBytes);
                    _ = unexpected.ReadByte();
                });
                lease.RecordPrimary(failure);
                await Assert.That(failure.ParamName).IsEqualTo("access");
            }

            await Assert.That(leaseHandle.IsClosed).IsTrue();
            await Assert.That(retainedDuplicate.IsClosed).IsFalse();
            await using var reopened = OfflineRegularFile.Open(path, FileAccess.Read, FileShare.None, bufferBytes);
            var actual = new byte[expected.Length];
            await reopened.ReadExactlyAsync(actual, TestContext.Current!.Execution.CancellationToken);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        });
    }

    private static void AssertLiveOwnerExcludesIndependentOpen(string path, int bufferBytes)
    {
        Assert.ThrowsExactly<IOException>(() =>
        {
            using var unexpected = OfflineRegularFile.Open(path, FileAccess.Read, FileShare.None, bufferBytes);
            _ = unexpected.ReadByte();
        });
    }

    private static async Task DisposeAndJoinAsync(FileStream owner, bool useAsync)
    {
        if (!useAsync)
        {
            DisposeSynchronous(owner);
            DisposeSynchronous(owner);
            await owner.DisposeAsync();
            return;
        }
        var first = owner.DisposeAsync().AsTask();
        var second = owner.DisposeAsync().AsTask();
        await Assert.That(second).IsSameReferenceAs(first);
        await Task.WhenAll(first, second);
        var repeated = owner.DisposeAsync().AsTask();
        await Assert.That(repeated).IsSameReferenceAs(first);
        await repeated;
    }

    private static void DisposeSynchronous(FileStream owner) => owner.Dispose();

    private static async Task WithFileAsync(Func<string, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "locked.bin");
        try
        {
            byte[] original = [0x31, 0x42, 0x53];
            await File.WriteAllBytesAsync(path, original, TestContext.Current!.Execution.CancellationToken);
            await action(path);
        }
        finally
        { Directory.Delete(root, recursive: true); }
    }
}
