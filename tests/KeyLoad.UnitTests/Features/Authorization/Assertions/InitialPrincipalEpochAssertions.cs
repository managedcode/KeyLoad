using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class InitialPrincipalEpochAssertions
{
    internal static string[] Image(ZoneTreeStore store, byte[]? omitted = null) => store.Read(view =>
    {
        var page = view.Scan([], 4096);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete principal lifecycle image exceeded its bound.");
        }
        return page.Records.Where(row => omitted is null || !row.Key.Span.SequenceEqual(omitted)
                && !row.Key.Span.SequenceEqual(KeySpace.ClockBytes))
            .Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });
    internal static async Task ObserveAsync(ZoneTreeStore store, Func<Task> action)
    {
        var image = Image(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(() => ConserveAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task ConserveAsync(ZoneTreeStore store, string[] image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(Image(store).SequenceEqual(image)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(store.Position).IsEqualTo(position), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(NativeSerialization.Serialize(store.Identity)
            .AsSpan().SequenceEqual(identity)).IsTrue(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task RefuseAsync(Action action, ErrorCode code)
        => await Assert.That(Assert.ThrowsExactly<KeyLoadException>(action).Code).IsEqualTo(code);
    internal static async Task CatalogAsync(InitialPrincipalEpochFixture fixture, ZoneTreeStore store)
    {
        await EqualAsync(store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(InitialPrincipalEpochFixture.Reader))),
            fixture.Principal(4L));
        await EqualAsync(store.Read(view => view.GetRecord<ApiKeyRecord>(KeySpace.ApiKey(InitialPrincipalEpochFixture.KeyId))),
            InitialPrincipalEpochFixture.Credential(replacement: true));
    }
    internal static async Task FailureAsync(OperationResult actual, OperationResult expected)
    {
        await EqualAsync(actual, expected);
        await Assert.That(actual.NativeValue).IsNull();
    }

}
