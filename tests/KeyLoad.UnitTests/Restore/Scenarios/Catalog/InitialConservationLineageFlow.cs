using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;
using P = KeyLoad.UnitTests.Features.BackupRestore.InitialConservationLineageProtocol;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class InitialConservationLineageFlow
{
    internal static Task RunAsync(CancellationToken token)
        => EventingArtifactFixture.RunAsync(async fixture =>
        {
            var source = fixture.Source;
            var definition = new ResourceDefinition(P.Collection, ResourceKind.Collection, source.Partition.TransactionDomainId);
            var creation = source.Database.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(), P.Root,
                source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(
                    new ConfigureResourceRequest(source.Partition.TenantId, source.Partition.DatabaseId, definition)));
            source.SubmitIssued(creation).Get<ResourceDefinition>();
            var lifetime = ResourceLifetimeIdentity.Create(creation, source.Partition.TenantId, source.Partition.DatabaseId, definition);
            var commandId = Guid.NewGuid();
            var command = new CommandRequest(commandId, source.Partition,
                [new PutDocument(P.Collection, P.Document, P.OriginalJson, P.Empty)]);
            var index = checked(source.ReadJournalCut().LastIndex + P.One);
            var receipt = source.Submit(OperationKind.Batch, command, id: commandId).Get<CommitReceipt>();
            var firstArchive = await InitialConservationArchiveFlow.CaptureAsync(source.Store, source.Database,
                Path.Combine(fixture.Root, P.FirstArchive), token);
            var expectedReceipt = new CommitReceipt(commandId, new(source.Store.Identity.Incarnation,
                source.Partition.AtomicPartitionId, index, firstArchive.Cut.Owner.PlacementEpoch),
                [new(P.PutKind, P.Collection, P.Document, P.One)], DurabilityProfile.ProcessDurable);
            await InitialConservationLineageAssertions.EqualAsync(expectedReceipt, receipt);
            await InitialConservationLineageAssertions.DocumentAsync(source.Database, source.Partition, lifetime, P.One, P.OriginalJson, token);
            var initial = source.Store.Read(view => ZoneTreeRetirementConservation.Capture(view,
                new ZoneTreeStoreOptions(source.Directory).ResolveExecutionOptions(source.StorageExecution), TimeProvider.System, token: token));
            await Assert.That(initial.Floor).IsNull();
            await Assert.That(initial.AuthorityRecords).IsEqualTo((long)P.Empty);
            using var first = new InitialConservationSlot(firstArchive, Path.Combine(fixture.Root, P.FirstTarget));
            var firstCompleted = first.Restore(token);
            await InitialConservationLineageAssertions.EqualAsync(first.ExpectedCompletion, firstCompleted);
            var empty = new ResourceRetirementAuthorityAccumulator().Complete();
            var origin = InitialConservationLineageAssertions.Origin(first, null, empty);
            await ColdReplayAsync(fixture, first, firstCompleted, [origin], lifetime, P.One, P.OriginalJson, token);
            var secondArchive = await InitialConservationArchiveFlow.CaptureNextAsync(fixture, first, lifetime, token);
            using var second = new InitialConservationSlot(secondArchive, Path.Combine(fixture.Root, P.SecondTarget));
            var firstFloor = InitialConservationLineageAssertions.Floor(origin);
            var inherited = new ResourceRetirementAuthoritySummary(firstFloor.AuthorityRecordCount, firstFloor.AuthorityDigest);
            var nested = InitialConservationLineageAssertions.Origin(second, firstFloor, inherited);
            var completed = second.Restore(token);
            await InitialConservationLineageAssertions.EqualAsync(second.ExpectedCompletion, completed);
            await ColdReplayAsync(fixture, second, completed, [origin, nested], lifetime, P.Two, P.UpdatedJson, token);
            await InitialConservationArchiveFlow.UnchangedAsync(firstArchive, source.Store, token);
            await WithStoreAsync(first.Destination, store => InitialConservationArchiveFlow.UnchangedAsync(secondArchive, store, token),
                UnitExecutionOptions.StorageExecution());
            await InitialConservationLineageAssertions.EqualAsync(receipt,
                source.Submit(OperationKind.Batch, command, id: commandId).Get<CommitReceipt>());
            await InitialConservationArchiveFlow.UnchangedAsync(firstArchive, source.Store, token);
        }, nativeAdmission: true);

    private static async Task ColdReplayAsync(EventingArtifactFixture fixture, InitialConservationSlot slot,
        NativeCatalogRestoreSlotCompletion completed, ResourceRetirementConservationSourceOriginV1[] origins,
        ResourceLifetimeV1 lifetime, long revision, string json, CancellationToken token)
    {
        for (var cold = P.Empty; cold < P.Two; cold++)
        {
            await SingleRecordRangesAsync(slot, origins, token);
            var before = NativeBackupJournalRepairAssertions.Read(slot.Destination, token);
            await WithStoreAsync(slot.Destination, async store =>
            {
                await Assert.That(store.Identity.NodeId).IsEqualTo(slot.Context.TargetNodeId);
                await Assert.That(store.Identity.Incarnation).IsEqualTo(slot.Mapping.Target.Incarnation);
                await Assert.That(store.Identity.DispatchPaused).IsTrue();
                var database = fixture.Source.CreateConfiguredDatabase(store);
                await InitialConservationLineageAssertions.DocumentAsync(database, fixture.Source.Partition, lifetime, revision, json, token);
                await InitialConservationLineageAssertions.StateAsync(store, slot, origins, origins[P.Empty].SourceOwner, token);
            }, UnitExecutionOptions.StorageExecution());
            await InitialConservationLineageAssertions.EqualAsync(completed, slot.Restore(token));
            await NativeBackupJournalRepairAssertions.SameCutAsync(before,
                NativeBackupJournalRepairAssertions.Read(slot.Destination, token));
            await NativeBackupJournalRepairAssertions.SameArchiveAsync(slot.Archive.Files, slot.Archive.Directory, token);
        }
    }

    private static Task SingleRecordRangesAsync(InitialConservationSlot slot,
        ResourceRetirementConservationSourceOriginV1[] origins, CancellationToken token)
        => WithStoreAsync(slot.Destination, async store =>
        {
            var options = new ZoneTreeStoreOptions(slot.Destination)
                .ResolveExecutionOptions(InitialConservationLineageAssertions.StorageExecution());
            var proof = store.Read(view => ZoneTreeRetirementConservation.Capture(view, options,
                TimeProvider.System, token: token));
            var summary = store.Read(view => ResourceRetirementAuthorityCapture.Capture(view,
                options.MaximumReadCutRecords, view.MaximumRangeRecords, options.MaxFrameBytes, token));
            var expectedFloor = InitialConservationLineageAssertions.Floor(origins[^P.One]);
            await Assert.That(summary.Records).IsEqualTo(expectedFloor.AuthorityRecordCount);
            await Assert.That(summary.Digest.Span.SequenceEqual(expectedFloor.AuthorityDigest.Span)).IsTrue();
            await InitialConservationLineageAssertions.EqualAsync(expectedFloor, proof.Floor!);
            await Assert.That(proof.AuthorityRecords).IsEqualTo((long)origins.Length);
            await Assert.That(proof.Digest.Span.SequenceEqual(expectedFloor.AuthorityDigest.Span)).IsTrue();
        }, InitialConservationLineageAssertions.StorageExecution());

    private static async Task WithStoreAsync(string directory, Func<ZoneTreeStore, Task> operation,
        IOptions<ZoneTreeStorageExecutionOptions> execution)
    {
        ZoneTreeStore? store = null;
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            store = new(new(directory), execution, UnitExecutionOptions.PointCacheExecution());
            await operation(store);
        }, failures);
        if (store is not null)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(store.Dispose, failures);
        }

        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
