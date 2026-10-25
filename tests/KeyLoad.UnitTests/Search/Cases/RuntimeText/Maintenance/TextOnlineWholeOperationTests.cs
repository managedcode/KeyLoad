using System.Collections.Immutable;
using KeyLoad.Clustering.Search;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextOnlineWholeOperationTests
{
    private const int TrackedBilingualRecords = 2;
    private const int CompetingSessions = 3;
    [Test]
    public async Task OrderedPublicationPreservesFullOriginalReceiptAndSameViewBilingualReads()
    {
        using var fixture = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await TextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new TextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var original = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(original.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(original.TrackedRecords).IsEqualTo(seed.Expected.Length);
        await TextBilingualAudit.VerifyAsync(runtime.Search, fixture, token);
        var replay = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await TextBilingualAudit.VerifyAsync(runtime.Search, fixture, token);
    }

    [Test]
    public async Task AlreadyCancelledActualMaintenanceJoinsBeforeFreshPublicationAndFullHealthyRead()
    {
        using var fixture = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await TextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new TextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancelled.CancelAsync();
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => runtime.PhaseAsync(fixture, Guid.NewGuid(), request,
            OnlineTextCapabilityKind.ResolveOriginal, TextOnlineTestRuntime.FreshExpiry(fixture), cancelled.Token));
        var healthy = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(healthy.CommandId).IsEqualTo(request.CommandId);
        await TextBilingualAudit.VerifyAsync(runtime.Search, fixture, token);
    }
    [Test]
    public async Task GenuineCompetingSessionsAreAdmittedWhileOnlyOneCaptureIsLiveThenJoinedAbortAllowsFullHealthyPublication()
    {
        using var fixture = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await TextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new TextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var owners = new List<Guid>();
        var expiry = TextOnlineTestRuntime.FreshExpiry(fixture);
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            for (var index = 0; index < CompetingSessions; index++)
            {
                var id = Guid.NewGuid();
                owners.Add(id);
                var competing = request with
                {
                    CommandId = Guid.NewGuid()
                };
                _ = await runtime.PhaseAsync(fixture, id, competing, OnlineTextCapabilityKind.ResolveOriginal, expiry, token);
                if (index == 0)
                {
                    _ = await runtime.PhaseAsync(fixture, id, competing, OnlineTextCapabilityKind.Capture, expiry, token);
                }
                else
                {
                    var refused = await Assert.ThrowsAsync<KeyLoadException>(() => runtime.PhaseAsync(fixture,
                        id, competing, OnlineTextCapabilityKind.Capture, expiry, token));
                    await Assert.That((refused ?? throw new InvalidOperationException()).Code).IsEqualTo(ErrorCode.ResourceExhausted);
                }
            }
        }, failures);
        foreach (var id in owners)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => runtime.Owner.AbortAsync(id), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        var result = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(result.CommandId).IsEqualTo(request.CommandId);
        await TextBilingualAudit.VerifyAsync(runtime.Search, fixture, token);
        var replay = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(OrleansSerialization.Serialize(result).SequenceEqual(OrleansSerialization.Serialize(replay))).IsTrue();
    }

    [Test]
    public async Task DeleteAndGenerationSwapPreserveOriginalReceiptAndCompleteDeletedExclusion()
    {
        using var fixture = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await TextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new TextOnlineTestRuntime(fixture);
        var first = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var original = await TextOnlineWholeFlow.RunAsync(fixture, runtime, first, token);
        await TextBilingualAudit.VerifyAsync(runtime.Search, fixture, token);
        var deleted = fixture.Commit(new DeleteDocument(TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId));
        await Assert.That(deleted.Mutations).HasSingleItem();
        var successor = first with
        {
            CommandId = Guid.NewGuid()
        };
        var published = await TextOnlineWholeFlow.RunAsync(fixture, runtime, successor, token);
        await Assert.That(published.TrackedRecords).IsEqualTo(TrackedBilingualRecords);
        await TextBilingualAudit.VerifyDeletedAsync(runtime.Search, fixture, token);
        var replay = await TextOnlineWholeFlow.RunAsync(fixture, runtime, first, token);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await TextBilingualAudit.VerifyDeletedAsync(runtime.Search, fixture, token);
    }

    [Test]
    public async Task ActualPinnedSnapshotAllowsUpdateDeleteThenOrderedDeltaPublishesCompleteLiteralCorpus()
    {
        using var fixture = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await TextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new TextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var result = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token, async () =>
        {
            var receipt = fixture.Commit(new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId,
                """{"text":"оновлено changed"}""", ExpectedRevision: 1),
                new DeleteDocument(TextBilingualAudit.Collection, TextBilingualAudit.EnglishId, ExpectedRevision: 1));
            MutationReceipt[] literal = [new("putDocument", TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId, 2),
                new("deleteDocument", TextBilingualAudit.Collection, TextBilingualAudit.EnglishId, 2)];
            await Assert.That(JsonDefaults.Serialize(receipt.Mutations).SequenceEqual(JsonDefaults.Serialize(literal.ToImmutableArray()))).IsTrue();
            foreach (var mutation in receipt.Mutations)
            {
                await Assert.That(mutation.CompositionReferences).IsEmpty();
            }
        });
        await Assert.That(result.PublishedCut.ThroughSequence).IsGreaterThan(result.BaseCut.ThroughSequence);
        await Assert.That(result.TrackedRecords).IsEqualTo(TrackedBilingualRecords);
        RankedDocument[] expected = [new(new(fixture.ExpectedReference(new(fixture.Partition, TextBilingualAudit.Collection,
            TextBilingualAudit.UkrainianId)), 2, """{"text":"оновлено changed"}""", false, []), 1d / 61d)];
        var actual = await runtime.Search.SearchAsync(TextMaintenanceTestValues.Principal,
            TextBilingualAudit.Request(fixture.Partition, "CHANGED"), token);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(await runtime.Search.SearchAsync(TextMaintenanceTestValues.Principal,
            TextBilingualAudit.Request(fixture.Partition, "ПРИВІТ"), token)).IsEmpty();
        await Assert.That(await runtime.Search.SearchAsync(TextMaintenanceTestValues.Principal,
            TextBilingualAudit.Request(fixture.Partition, "hello"), token)).IsEmpty();
    }

    [Test]
    public async Task RealOldReaderRetainsItsOriginalFilesAcrossOnlineSwapUntilJoinedOwnerShutdown()
    {
        using var fixture = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await TextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new TextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var first = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        var budget = new KeyLoad.Core.ReadExecutionBudget(
            fixture.Database.EvaluationClock, token);
        var publication = fixture.Database.ReadOnlineTextOriginalPublication(TextMaintenanceTestValues.Principal, request, budget)!;
        var path = Path.Combine(fixture.Directory, KeyLoad.Server.Search.TextOnlineRoot.DirectoryName, publication.Authority.Leaf);
        using (var held = runtime.BorrowCurrent(fixture, TextBilingualAudit.Request(fixture.Partition, "hello"), budget))
        {
            held.BeginRecord(seed.Expected[0].Reference, seed.Expected[0].Revision);
            held.ObserveToken("hello");
            held.ObserveToken("world");
            held.BeginRecord(seed.Expected[1].Reference, seed.Expected[1].Revision);
            held.ObserveToken("привіт");
            held.ObserveToken("світ");
            fixture.Commit(new DeleteDocument(TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId));
            _ = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request with
            {
                CommandId = Guid.NewGuid()
            }, token);
            held.VerifyCandidates(["привіт"], [seed.Expected[1].Reference], budget);
            await Assert.That(Directory.Exists(path)).IsTrue();
            await TextBilingualAudit.VerifyDeletedAsync(runtime.Search, fixture, token);
        }
        await runtime.DisposeAsync();
        await Assert.That(Directory.Exists(path)).IsFalse();
        await using var recovered = new TextOnlineTestRuntime(fixture);
        await TextBilingualAudit.VerifyDeletedAsync(recovered.Search, fixture, token);
        var replay = await TextOnlineWholeFlow.RunAsync(fixture, recovered, request, token);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(first))).IsTrue();
        await TextBilingualAudit.VerifyDeletedAsync(recovered.Search, fixture, token);
    }

    [Test]
    public async Task ExistingOnlinePublicationSurvivesActualCapturedReplacementCancellationAndJoinedAbortBeforeFreshFullReplacementReplayAndCold()
        => await TextOnlineReplacementRollbackFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);

}
