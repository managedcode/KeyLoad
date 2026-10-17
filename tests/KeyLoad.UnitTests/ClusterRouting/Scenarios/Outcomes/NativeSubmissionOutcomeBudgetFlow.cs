using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeSubmissionOutcomeBudgetFlow
{
    private const string TestValueKey = "c1-budget-test-value";
    private const int ValueBytes = 4096;
    private const int ExpectedReadBytes = 32768;
    private const int OversizeValueBytes = 8193;
    private const int NoReads = 0;
    private const int OverboundIncrement = 1;
    private const string MissingRead = "The original native budget read did not return its complete result.";

    internal static async Task RepeatedAsync(C1OutcomeInspectionFixture fixture, bool owned)
    {
        var seed = await NativeSubmissionOutcomeProcessSeed.CreateAsync(fixture).ConfigureAwait(false);
        var key = KeyCodec.Encode(TestValueKey);
        var original = new byte[ValueBytes];
        NativeSubmissionOutcomeProcessCut.ReplaceOwnedRow(fixture, key, original);
        var before = NativeSubmissionOutcomeProcessCut.Read(fixture);
        var charge = checked(key.Length + original.Length);
        var allowed = ExpectedReadBytes / charge;
        var result = Read(fixture, raw => Repeated(raw, key, original, allowed, owned));
        await Assert.That(allowed > NoReads).IsTrue();
        await Assert.That(checked((long)allowed * charge) <= ExpectedReadBytes).IsTrue();
        await Assert.That(checked((long)(allowed + OverboundIncrement) * charge)
            > ExpectedReadBytes).IsTrue();
        await Assert.That(result.Reads).IsEqualTo(allowed);
        await Assert.That(result.Exact).IsTrue();
        await Assert.That(result.DeniedCallback).IsFalse();
        await before.RequireUnchangedAsync(fixture).ConfigureAwait(false);
        await NativeSubmissionOutcomeProcessAssertions.RequireHealthyAsync(fixture, seed).ConfigureAwait(false);
    }

    private static (int Reads, bool Exact, bool DeniedCallback) Repeated(IKeyValueView raw,
        byte[] key, byte[] original, int allowed, bool owned)
    {
        var view = new C1SubmissionOutcomeInspectionView(raw);
        var reads = NoReads;
        var exact = true;
        for (var ordinal = NoReads; ordinal < allowed; ordinal++)
        {
            if (owned)
            {
                var actual = view.ReadOwnedValue(key);
                exact &= actual is not null && actual.AsSpan().SequenceEqual(original);
                if (actual is not null)
                {
                    reads++;
                }
            }
            else
            {
                var found = view.ReadValue(key, bytes => { exact &= bytes.SequenceEqual(original); reads++; });
                exact &= found;
            }
        }
        var deniedCallback = false;
        Assert.ThrowsExactly<InvalidDataException>(() => view.ReadValue(key, _ => deniedCallback = true));
        Assert.ThrowsExactly<InvalidDataException>(() => view.ReadOwnedValue(key));
        return (reads, exact, deniedCallback);
    }

    internal static async Task OversizeAsync(C1OutcomeInspectionFixture fixture)
    {
        var seed = await NativeSubmissionOutcomeProcessSeed.CreateAsync(fixture).ConfigureAwait(false);
        var key = KeyCodec.Encode(TestValueKey);
        var original = new byte[ValueBytes];
        NativeSubmissionOutcomeProcessCut.ReplaceOwnedRow(fixture, key,
            new byte[OversizeValueBytes]);
        var before = NativeSubmissionOutcomeProcessCut.Read(fixture);
        var deniedCallback = Read(fixture, raw =>
        {
            var view = new C1SubmissionOutcomeInspectionView(raw);
            var invoked = false;
            Assert.ThrowsExactly<InvalidDataException>(() => view.ReadValue(key, _ => invoked = true));
            Assert.ThrowsExactly<InvalidDataException>(() => view.ReadOwnedValue(key));
            return invoked;
        });
        await Assert.That(deniedCallback).IsFalse();
        await before.RequireUnchangedAsync(fixture).ConfigureAwait(false);
        NativeSubmissionOutcomeProcessCut.ReplaceOwnedRow(fixture, key, original);
        var restored = NativeSubmissionOutcomeProcessCut.Read(fixture);
        var healthy = Read(fixture, raw => new C1SubmissionOutcomeInspectionView(raw).ReadOwnedValue(key));
        await Assert.That(healthy is not null && healthy.AsSpan().SequenceEqual(original)).IsTrue();
        await restored.RequireUnchangedAsync(fixture).ConfigureAwait(false);
        await NativeSubmissionOutcomeProcessAssertions.RequireHealthyAsync(fixture, seed).ConfigureAwait(false);
    }

    private static T Read<T>(C1OutcomeInspectionFixture fixture, Func<IKeyValueView, T> read)
    {
        fixture.CloseStore();
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        T? result = default;
        var returned = false;
        ServerFailureObserver.Observe(() =>
        {
            var opened = ZoneTreeExistingStore.Open(new(fixture.DirectoryPath)
            {
                Incarnation = fixture.Incarnation
            },
                fixture.Identity.NodeId, UnitExecutionOptions.StorageExecution());
            store = opened;
            result = opened.Read(read);
            returned = true;
        }, failures);
        ServerFailureObserver.Observe(() => store?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return returned ? result! : throw new InvalidOperationException(MissingRead);
    }
}
