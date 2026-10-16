using C = KeyLoad.UnitTests.Features.DocumentStorage.RemoteDocumentNativeFixture;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentLifetimeNativeReadTests
{
    private const long Third = 3;
    private const long Fourth = 4;
    private static readonly TimeSpan OriginalExpiry = TimeSpan.FromSeconds(5);

    [Test]
    public Task ActualDistinctD1AndIndependentEpochsUseBothNativeAuthoritiesAndTwoColdOwners()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            fixture.Seed();
            fixture.GrantDestination();
            await RemoteDocumentNativeAssertions.ReceiptAsync(fixture);
            var frame = DocumentLifetimeNativeRead.Capture(fixture);
            await Assert.That(frame.Coverage.Lifetimes.Single().Incarnation)
                .IsNotEqualTo(DocumentLifetimeNativeRead.TargetLifetime(fixture).Incarnation);
            for (var cold = default(int); cold < Third; cold++)
            {
                var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
                var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
                var position = fixture.Destination.Store.Position;
                frame = DocumentLifetimeNativeRead.Capture(fixture);
                await RemoteDocumentNativeAssertions.DocumentAsync(DocumentLifetimeNativeRead.Read(fixture, frame), fixture.Destination, position);
                fixture.Source.Database.ValidateRemoteDocumentReadV2(frame, DocumentLifetimeNativeRead.Work(fixture.Source.Database));
                await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Source).SequenceEqual(source)).IsTrue();
                await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(destination)).IsTrue();
                var replay = fixture.Destination.Database.Apply(fixture.Write).Get<CommitReceipt>();
                await Assert.That(NativeSerialization.Serialize(replay).AsSpan().SequenceEqual(NativeSerialization.Serialize(fixture.Receipt))).IsTrue();
                if (cold < C.Second)
                {
                    fixture.Source.Reopen();
                    fixture.Destination.Reopen();
                }
            }
        });

    [Test]
    public Task ValidWrongCreationAndCurrentSourcePolicyChangeAreWholeFrameFailures()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            fixture.Seed();
            fixture.GrantDestination();
            var frame = DocumentLifetimeNativeRead.Capture(fixture);
            var wrong = frame with
            {
                Coverage = frame.Coverage with
                {
                    Lifetimes = [DocumentLifetimeNativeRead.TargetLifetime(fixture)]
                }
            };
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Source.Database.ValidateRemoteDocumentReadV2(
                wrong, DocumentLifetimeNativeRead.Work(fixture.Source.Database)));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
            fixture.Source.AddPrincipal(DocumentLifetimeNativeRead.Definition(C.Second, raw: false));
            var changed = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Source.Database.ValidateRemoteDocumentReadV2(
                frame, DocumentLifetimeNativeRead.Work(fixture.Source.Database)));
            await Assert.That(changed.Code).IsEqualTo(ErrorCode.OwnershipLost);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            OwnedDocumentReadResultV1? observed = null;
            _ = Assert.ThrowsExactly<OperationCanceledException>(() => observed = DocumentLifetimeNativeRead.Read(fixture,
                DocumentLifetimeNativeRead.Capture(fixture), cancelled.Token));
            await Assert.That(observed).IsNull();
        });

    [Test]
    public Task SameEpochDifferentRawGrantsIntersectInBothDirectionsWithoutBroadening()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            fixture.Seed();
            fixture.GrantDestination();
            fixture.Source.AddPrincipal(DocumentLifetimeNativeRead.Definition(C.Second, raw: false));
            fixture.Source.AddPrincipal(DocumentLifetimeNativeRead.Definition(Third, raw: false));
            fixture.Destination.AddPrincipal(DocumentLifetimeNativeRead.Definition(Third, raw: true));
            var first = DocumentLifetimeNativeRead.Read(fixture, DocumentLifetimeNativeRead.Capture(fixture));
            var expected = new DocumentResult(C.Reference, C.First, C.ProjectedJson, true, [C.Secret]);
            await Assert.That(JsonDefaults.Serialize(first.Document).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            fixture.Source.AddPrincipal(DocumentLifetimeNativeRead.Definition(Fourth, raw: true));
            fixture.Destination.AddPrincipal(DocumentLifetimeNativeRead.Definition(Fourth, raw: false));
            var second = DocumentLifetimeNativeRead.Read(fixture, DocumentLifetimeNativeRead.Capture(fixture));
            await Assert.That(JsonDefaults.Serialize(second.Document).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        });
    [Test]
    public Task GenuineOriginalExpiryRefusesBeforeDocumentAndLeavesBothStoresUnchanged()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            fixture.Seed();
            fixture.GrantDestination();
            var expiry = fixture.Source.Database.EvaluationClock.GetUtcNow() + OriginalExpiry;
            var frame = fixture.Source.Database.CaptureRemoteDocumentReadV2(C.Reader, C.Reference, expiry,
                DocumentLifetimeNativeRead.Work(fixture.Source.Database)) ?? throw new InvalidOperationException(C.Collection);
            var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var target = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var clock = fixture.Destination.Database.EvaluationClock;
            var remaining = expiry - clock.GetUtcNow();
            while (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, clock, TestContext.Current!.Execution.CancellationToken);
                remaining = expiry - clock.GetUtcNow();
            }
            await Assert.That(clock.GetUtcNow() >= expiry).IsTrue();
            OwnedDocumentReadResultV1? result = null;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => result = DocumentLifetimeNativeRead.Read(fixture, frame));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(result).IsNull();
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Source).SequenceEqual(source)).IsTrue();
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(target)).IsTrue();
        });

}
