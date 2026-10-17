using System.Runtime.InteropServices;
using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class NativeWriteThroughJournalCut
{
    internal const long SeedPosition = 1;
    internal const long HealthyPosition = 2;
    private const int CompleteRecords = 2;
    private const int LinuxSynchronous = 0x101000;
    private const int LinuxX64NoFollow = 0x20000;
    private const int LinuxArm64NoFollow = 0x8000;
    private const string UnsupportedArchitecture = "The native file flags require the supported architecture.";
    private const int MacSynchronous = 0x80;
    private const int LinuxNonBlocking = 0x800;
    private const int MacNonBlocking = 4;
    private const string HealthyKey = "journal/write-through-healthy";
    private const string HealthyValue = "canonical durable native continuation";

    internal static async Task FlagsAsync(FileStream stream, int originalBclFlags)
    {
        var flags = NativeOpenStatusFlags.Read(stream);
        var synchronous = OperatingSystem.IsMacOS() ? MacSynchronous : LinuxSynchronous;
        var nonblocking = OperatingSystem.IsMacOS() ? MacNonBlocking : LinuxNonBlocking;
        await Assert.That(originalBclFlags & synchronous).IsEqualTo(synchronous);
        await Assert.That(flags & synchronous).IsEqualTo(originalBclFlags & synchronous);
        await Assert.That(flags & nonblocking).IsEqualTo(nonblocking);
        if (OperatingSystem.IsLinux())
        {
            var nofollow = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => LinuxX64NoFollow,
                Architecture.Arm64 => LinuxArm64NoFollow,
                _ => throw new PlatformNotSupportedException(UnsupportedArchitecture)
            };
            await Assert.That(flags & nofollow).IsEqualTo(nofollow);
        }
    }

    internal static void CommitHealthy(ZoneTreeStore store)
        => store.Commit((tx, _) =>
        {
            tx.Put(Encoding.UTF8.GetBytes(HealthyKey), Encoding.UTF8.GetBytes(HealthyValue));
            return true;
        });

    internal static async Task RowsAsync(ZoneTreeStore store, ZoneTreeExistingStoreFixture files, bool healthy)
    {
        await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(files.Identity))).IsTrue();
        await Assert.That(store.Position).IsEqualTo(healthy ? HealthyPosition : SeedPosition);
        var expected = new List<KeyValueRecord> { new(files.Key, files.Value) };
        if (healthy)
        {
            expected.Add(new(Encoding.UTF8.GetBytes(HealthyKey), Encoding.UTF8.GetBytes(HealthyValue)));
        }
        var actual = store.Read(view => view.Scan([], CompleteRecords));
        await Assert.That(actual.HasMore).IsFalse();
        await Assert.That(actual.Records.Length).IsEqualTo(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            await Assert.That(actual.Records[index].Key.Span.SequenceEqual(expected[index].Key.Span)).IsTrue();
            await Assert.That(actual.Records[index].Value.Span.SequenceEqual(expected[index].Value.Span)).IsTrue();
        }
    }
}
