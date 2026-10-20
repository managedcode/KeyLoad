using KeyLoad.Core.Features.Search;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class ProjectionDigestImage
{
    internal static string[] Domain(EventProjectionFixture fixture)
    {
        string[] families = ["document", "document-epoch", "index", "unique", "vector",
            VectorProjectionKeys.LineageSpace, VectorProjectionKeys.EffectSpace,
            "stream-head", "event", "event-id", "event-feed", "event-sequence", "outbox", "outbox-head"];
        var prefixes = families.Select(family => Convert.ToHexString(KeySpace.Partition(family, fixture.Partition))).ToArray();
        return QueueWholeFlowStorage.Bytes(fixture.Harness.Store).Where(row => prefixes.Any(prefix =>
            row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }

    internal static byte[] Damaged(byte[] original, bool effect)
    {
        if (effect)
        {
            var value = OrleansSerialization.Deserialize<VectorProjectionEffect>(original);
            return OrleansSerialization.Serialize(value with
            {
                Fingerprint = (value.Fingerprint[0] == '0' ? "1" : "0") + value.Fingerprint[1..]
            });
        }
        var vector = OrleansSerialization.Deserialize<VectorRecord>(original);
        return OrleansSerialization.Serialize(vector with
        {
            Values = [0f, 1f]
        });
    }

    internal static void Write(EventProjectionFixture fixture, byte[] key, byte[] value)
        => fixture.Harness.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, value);
            return true;
        });

    internal static string[] Replace(string[] image, byte[] key, byte[] original, byte[] damaged)
    {
        var prefix = Convert.ToHexString(key) + ":";
        var before = prefix + Convert.ToHexString(original);
        if (!string.Equals(image.Single(row => row.StartsWith(prefix, StringComparison.Ordinal)), before, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The captured original digest row is not the exact execution input.");
        }
        return image.Select(row => string.Equals(row, before, StringComparison.Ordinal)
            ? prefix + Convert.ToHexString(damaged) : row).ToArray();
    }

    internal static async Task ObserveAsync(EventProjectionFixture fixture, Func<Task> operation)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var cut = fixture.Harness.Store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => ClassificationLineageWholeAssertions.UnchangedAsync(
            fixture, image, cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ColdAsync(EventProjectionFixture fixture, string[] image, long cut)
    {
        var identity = OrleansSerialization.Serialize(fixture.Harness.Store.Identity);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(fixture.Harness.Reopen, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(OrleansSerialization.Serialize(fixture.Harness.Store.Identity).AsSpan().SequenceEqual(identity)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(() => ClassificationLineageWholeAssertions.UnchangedAsync(
            fixture, image, cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
