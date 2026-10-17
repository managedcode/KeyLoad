using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeReadCutCleanupTests
{
    [Test]
    public async Task AcCut003CleanupRetainsEmptyAndNestedAggregateFailures()
    {
        var empty = new AggregateException();
        var nestedLeaf = new InvalidOperationException();
        var nested = new AggregateException(new AggregateException(nestedLeaf));
        var failures = new List<Exception>();

        ZoneTreeReadCutCleanup.Capture(() => throw empty, failures);
        ZoneTreeReadCutCleanup.Capture(() => throw nested, failures);

        await Assert.That(failures.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(failures[0], empty)).IsTrue();
        await Assert.That(ReferenceEquals(failures[1], nested)).IsTrue();
    }
}
