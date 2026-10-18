using KeyLoad.Core.Features.Search;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25CorpusOptionalHeadTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task InvalidOptionalNativeHeadDeclinesReuseWithoutChangingCanonicalSearchThenRestoredHeadIsHealthy(bool malformedBytes)
        => Bm25CorpusPublicationFlow.RunAsync(async flow =>
        {
            var original = flow.Capture(Guid.NewGuid(), null);
            flow.Publish(original);
            var current = Bm25CorpusPublicationFlow.Phase(original).Current;
            var key = Bm25CorpusKeys.Current(current);
            var bytes = flow.Database.Store.Read(view => view.ReadOwnedValue(key))
                ?? throw new InvalidOperationException("The actual published head is missing.");
            var invalid = malformedBytes ? new byte[] { 0 }
                : NativeSerialization.Serialize(current with
                {
                    FormatVersion = 0
                });
            var failures = new List<Exception>();
            var restore = false;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                restore = true;
                flow.Database.Store.Commit((transaction, _) => { transaction.Put(key, invalid); return true; });
                var before = CanonicalStoreCut.Read(flow.Database);
                await Assert.That(flow.Current()).IsNull();
                await Assert.That((await flow.SearchAsync()).Single().Document.Reference.Id).IsEqualTo("first");
                await before.RequireAsync(CanonicalStoreCut.Read(flow.Database));
            }, failures);
            if (restore)
            {
                ServerFailureObserver.Observe(() => flow.Database.Store.Commit((transaction, _) =>
                {
                    transaction.Put(key, bytes);
                    return true;
                }), failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            var restored = flow.Database.Store.Read(view => view.ReadOwnedValue(key))!;
            await NativeReplayResultAssertions.BytesAsync(bytes, restored);
            var healthyBefore = CanonicalStoreCut.Read(flow.Database);
            await Assert.That(flow.Current()!.DocumentCount).IsEqualTo(1);
            await Assert.That(flow.Current()!.TotalLength).IsEqualTo(1L);
            await Assert.That((await flow.SearchAsync()).Single().Document.Reference.Id).IsEqualTo("first");
            await healthyBefore.RequireAsync(CanonicalStoreCut.Read(flow.Database));
        });
}
