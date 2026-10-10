using System.Collections.Immutable;
using KeyLoad.Orleans.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextOnlineWholeOperationTests
{
    private const int TrackedBilingualRecords = 2;
    private const int FirstCapturedOwner = 0;
    [Test]
    public async Task NativeOrderedPublicationPreservesFullOriginalReceiptAndSameViewBilingualReads()
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new NativeTextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var original = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(original.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(original.TrackedRecords).IsEqualTo(seed.Expected.Length);
        await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
        var replay = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
    }

    [Test]
    public async Task AlreadyCancelledActualMaintenanceJoinsBeforeFreshNativePublicationAndFullHealthyRead()
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new NativeTextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancelled.CancelAsync();
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => runtime.PhaseAsync(fixture, Guid.NewGuid(), request,
            OnlineTextCapabilityKind.ResolveOriginal, runtime.FreshExpiry(fixture), cancelled.Token));
        var healthy = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(healthy.CommandId).IsEqualTo(request.CommandId);
        await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
    }
    [Test]
    public async Task GenuineCompetingCapturedMaintenanceRefusesAtSharedLeaseCeilingThenJoinedAbortAllowsFullHealthyPublication()
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new NativeTextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var owners = new List<Guid>();
        var expiry = runtime.FreshExpiry(fixture);
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            for (var index = 0; index < runtime.Options.NativeText.Value.MaximumActiveLeases; index++)
            {
                var id = Guid.NewGuid();
                owners.Add(id);
                var competing = request with { CommandId = Guid.NewGuid() };
                _ = await runtime.PhaseAsync(fixture, id, competing, OnlineTextCapabilityKind.ResolveOriginal, expiry, token);
                if (index == FirstCapturedOwner)
                { _ = await runtime.PhaseAsync(fixture, id, competing, OnlineTextCapabilityKind.Capture, expiry, token); }
                else
                {
                    var refused = await Assert.ThrowsAsync<KeyLoadException>(() => runtime.PhaseAsync(fixture,
                        id, competing, OnlineTextCapabilityKind.Capture, expiry, token));
                    await Assert.That((refused ?? throw new InvalidOperationException()).Code).IsEqualTo(ErrorCode.ResourceExhausted);
                }
            }
            var error = await Assert.ThrowsAsync<KeyLoadException>(() => runtime.PhaseAsync(fixture,
                Guid.NewGuid(), request, OnlineTextCapabilityKind.ResolveOriginal, expiry, token));
            await Assert.That((error ?? throw new InvalidOperationException()).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        }, failures);
        foreach (var id in owners)
        { await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => runtime.Owner.AbortAsync(id), failures); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        var result = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(result.CommandId).IsEqualTo(request.CommandId);
        await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
        var replay = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(NativeSerialization.Serialize(result).SequenceEqual(NativeSerialization.Serialize(replay))).IsTrue();
    }

    [Test]
    public async Task NativeDeleteAndGenerationSwapPreserveOriginalReceiptAndCompleteDeletedExclusion()
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new NativeTextOnlineTestRuntime(fixture);
        var first = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var original = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, first, token);
        await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
        var deleted = fixture.Commit(new DeleteDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId));
        await Assert.That(deleted.Mutations).HasSingleItem();
        var successor = first with { CommandId = Guid.NewGuid() };
        var published = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, successor, token);
        await Assert.That(published.TrackedRecords).IsEqualTo(TrackedBilingualRecords);
        await NativeTextBilingualAudit.VerifyDeletedAsync(runtime.Search, fixture.Partition, token);
        var replay = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, first, token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await NativeTextBilingualAudit.VerifyDeletedAsync(runtime.Search, fixture.Partition, token);
    }

    [Test]
    public async Task ActualPinnedSnapshotAllowsNativeUpdateDeleteThenOrderedDeltaPublishesCompleteLiteralCorpus()
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new NativeTextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var result = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token, async () =>
        {
            var receipt = fixture.Commit(new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId,
                """{"text":"оновлено changed"}""", ExpectedRevision: 1),
                new DeleteDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId, ExpectedRevision: 1));
            MutationReceipt[] literal = [new("putDocument", NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId, 2),
                new("deleteDocument", NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId, 2)];
            await Assert.That(JsonDefaults.Serialize(receipt.Mutations).SequenceEqual(JsonDefaults.Serialize(literal.ToImmutableArray()))).IsTrue();
            foreach (var mutation in receipt.Mutations)
            { await Assert.That(mutation.CompositionReferences).IsEmpty(); }
        });
        await Assert.That(result.PublishedCut.ThroughSequence).IsGreaterThan(result.BaseCut.ThroughSequence);
        await Assert.That(result.TrackedRecords).IsEqualTo(TrackedBilingualRecords);
        RankedDocument[] expected = [new(new(new(fixture.Partition, NativeTextBilingualAudit.Collection,
            NativeTextBilingualAudit.UkrainianId), 2, """{"text":"оновлено changed"}""", false, []), 1d / 61d)];
        var actual = await runtime.Search.SearchAsync(NativeTextMaintenanceTestValues.Principal,
            NativeTextBilingualAudit.Request(fixture.Partition, "CHANGED"), token);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(await runtime.Search.SearchAsync(NativeTextMaintenanceTestValues.Principal,
            NativeTextBilingualAudit.Request(fixture.Partition, "ПРИВІТ"), token)).IsEmpty();
        await Assert.That(await runtime.Search.SearchAsync(NativeTextMaintenanceTestValues.Principal,
            NativeTextBilingualAudit.Request(fixture.Partition, "hello"), token)).IsEmpty();
    }

    [Test]
    public async Task RealOldNativeReaderRetainsItsOriginalFilesAcrossOnlineSwapUntilJoinedOwnerShutdown()
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new NativeTextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var first = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        var budget = new KeyLoad.Core.ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits),
            fixture.Database.EvaluationClock, token);
        var publication = fixture.Database.ReadOnlineTextOriginalPublication(NativeTextMaintenanceTestValues.Principal, request, budget)!;
        var path = Path.Combine(fixture.Directory, KeyLoad.Server.Features.Search.NativeTextOnlineRoot.DirectoryName, publication.Authority.Leaf);
        using (var held = runtime.BorrowCurrent(fixture, NativeTextBilingualAudit.Request(fixture.Partition, "hello"), budget))
        {
            held.BeginRecord(seed.Expected[0].Reference, seed.Expected[0].Revision);
            held.ObserveToken("hello");
            held.ObserveToken("world");
            held.BeginRecord(seed.Expected[1].Reference, seed.Expected[1].Revision);
            held.ObserveToken("привіт");
            held.ObserveToken("світ");
            fixture.Commit(new DeleteDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId));
            _ = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request with { CommandId = Guid.NewGuid() }, token);
            held.VerifyCandidates(["привіт"], [seed.Expected[1].Reference], budget);
            await Assert.That(Directory.Exists(path)).IsTrue();
            await NativeTextBilingualAudit.VerifyDeletedAsync(runtime.Search, fixture.Partition, token);
        }
        await runtime.DisposeAsync();
        await Assert.That(Directory.Exists(path)).IsFalse();
        await using var recovered = new NativeTextOnlineTestRuntime(fixture);
        await NativeTextBilingualAudit.VerifyDeletedAsync(recovered.Search, fixture.Partition, token);
        var replay = await NativeTextOnlineWholeFlow.RunAsync(fixture, recovered, request, token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(first))).IsTrue();
        await NativeTextBilingualAudit.VerifyDeletedAsync(recovered.Search, fixture.Partition, token);
    }

    [Test]
    public async Task ExistingOnlinePublicationSurvivesActualCapturedReplacementCancellationAndJoinedAbortBeforeFreshFullReplacementReplayAndCold()
        => await NativeTextOnlineReplacementRollbackFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);

}
