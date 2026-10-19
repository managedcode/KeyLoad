using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleReplayCutAssertions
{
    internal static QueueLifecycleConditionCut Capture(IAtomicStore store)
    {
        var image = store.Read(view =>
        {
            var page = view.Scan([], 4096);
            if (page.HasMore)
            {
                throw new InvalidOperationException("Whole store state exceeded the fixture bound.");
            }
            return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" +
                Convert.ToHexString(row.Value.Span)).ToArray();
        });
        return new(store.Position, OrleansSerialization.Serialize(store.Identity), image);
    }

    internal static async Task SameAsync(IAtomicStore store, QueueLifecycleConditionCut expected)
    {
        var actual = Capture(store);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Identity.SequenceEqual(expected.Identity)).IsTrue();
        await Assert.That(actual.Image).IsEquivalentTo(expected.Image, CollectionOrdering.Matching);
    }
}
