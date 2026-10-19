using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class ValueHeaderAssertions
{
    private const int NoDisclosure = 0;
    private const string Invalid = "A execution stored record has an invalid value header.";

    internal static async Task RequireAsync(ZoneTreeStoreRuntime runtime, ZoneTreeStore store, ZoneTreeExistingStoreFixture files)
    {
        await Assert.That(runtime.PointCache).IsNull();
        var identity = await File.ReadAllBytesAsync(files.IdentityPath);
        var journal = await File.ReadAllBytesAsync(files.JournalPath);
        var observed = 0;
        var delivered = 0;
        await FailureAsync(Assert.ThrowsExactly<KeyLoadException>(() =>
            store.Read(view => view.ReadOwnedValue(files.Key))));
        await UnchangedAsync(runtime, store, files, identity, journal);
        await FailureAsync(Assert.ThrowsExactly<KeyLoadException>(() => store.Read(view => view.ReadValue(files.Key,
            _ => delivered++, _ => observed++))));
        await UnchangedAsync(runtime, store, files, identity, journal);
        foreach (var reverse in new[] { false, true })
        {
            await FailureAsync(Assert.ThrowsExactly<KeyLoadException>(() => store.Read(view => reverse
                ? view.VisitReverseRange([], ValueHeaderRecords.CompleteRecords,
                    (_, _) =>
                    {
                        delivered++;
                        return true;
                    }, observer: _ => observed++)
                : view.VisitRange([], ValueHeaderRecords.CompleteRecords,
                    (_, _) =>
                    {
                        delivered++;
                        return true;
                    }, observer: _ => observed++))));
            await UnchangedAsync(runtime, store, files, identity, journal);
        }
        await Assert.That(observed).IsEqualTo(NoDisclosure);
        await Assert.That(delivered).IsEqualTo(NoDisclosure);
        await ValueHeaderSnapshot.RequireAsync(store, files);
        await UnchangedAsync(runtime, store, files, identity, journal);
    }

    private static async Task FailureAsync(KeyLoadException failure)
    {
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(Invalid);
    }

    private static async Task UnchangedAsync(ZoneTreeStoreRuntime runtime, ZoneTreeStore store, ZoneTreeExistingStoreFixture files,
        byte[] identity, byte[] journal)
    {
        await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(files.Identity))).IsTrue();
        await Assert.That(store.Position).IsEqualTo(ValueHeaderRecords.SeedPosition);
        var raw = store.Read(_ => runtime.Tree.TryGet(files.Key, out var value) ? value.ToArray() : null);
        await Assert.That(raw).IsNotNull();
        await Assert.That(raw!.AsSpan().SequenceEqual(ValueHeaderRecords.InvalidBytes(files))).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(files.IdentityPath)).AsSpan().SequenceEqual(identity)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(files.JournalPath)).AsSpan().SequenceEqual(journal)).IsTrue();
    }
}
