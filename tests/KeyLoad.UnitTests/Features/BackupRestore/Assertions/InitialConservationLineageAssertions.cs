using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.BackupRestore.InitialConservationLineageProtocol;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class InitialConservationLineageAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();

    internal static async Task DocumentAsync(DatabaseEngine database, PartitionRef partition,
        long revision, string json, CancellationToken token)
    {
        var reference = new EntityRef(partition, P.Collection, P.Document);
        await EqualAsync(new DocumentResult(reference, revision, json, false, []),
            database.GetDocument(P.Root, reference, cancellationToken: token)!);
    }

    internal static ResourceRetirementConservationSourceOriginV1 Origin(InitialConservationSlot slot,
        ResourceRetirementConservationFloorV1? sourceFloor, ResourceRetirementAuthoritySummary sourceSummary)
        => new(P.One, slot.Mapping.Source, slot.Archive.Identity.NodeId, slot.Archive.Identity.Incarnation,
            slot.Archive.Capture.Position, slot.Archive.Cut.AppliedIndex, sourceFloor,
            sourceSummary.Records, sourceSummary.Digest, slot.Mapping.Target);

    internal static ResourceRetirementConservationFloorV1 Floor(ResourceRetirementConservationSourceOriginV1 origin)
    {
        var key = ResourceRetirementConservationOriginKeys.Row(origin.SourceOwner, origin.TargetOwner);
        var accumulator = new ResourceRetirementAuthorityAccumulator();
        accumulator.Add(new(origin.SourceAuthorityRecordCount, origin.SourceAuthorityDigest));
        accumulator.Add(key, NativeSerialization.Serialize(origin));
        var summary = accumulator.Complete();
        return ResourceRetirementConservation.Initial(origin.TargetOwner) with
        {
            AuthorityRecordCount = summary.Records,
            AuthorityDigest = summary.Digest,
            BaselineOriginKey = key
        };
    }

    internal static async Task StateAsync(ZoneTreeStore store, InitialConservationSlot slot,
        ResourceRetirementConservationSourceOriginV1[] origins, PhysicalShardRecord neverIssuedOwner,
        CancellationToken token)
    {
        var rows = EventingArtifactState.FullBytes(store);
        var position = store.Position;
        var expectedFloor = Floor(origins[^P.One]);
        var options = new ZoneTreeStoreOptions(slot.Destination).ResolveExecutionOptions(UnitExecutionOptions.StorageExecution());
        var proof = store.Read(view => ZoneTreeRetirementConservation.Capture(view, options, TimeProvider.System, token: token));
        await EqualAsync(expectedFloor, proof.Floor!);
        await Assert.That(proof.AuthorityRecords).IsEqualTo((long)origins.Length);
        await Assert.That(proof.Digest.Span.SequenceEqual(expectedFloor.AuthorityDigest.Span)).IsTrue();
        foreach (var origin in origins)
        {
            var bytes = store.Read(view => view.ReadOwnedValue(
                ResourceRetirementConservationOriginKeys.Row(origin.SourceOwner, origin.TargetOwner)));
            await Assert.That(bytes is not null && bytes.AsSpan().SequenceEqual(NativeSerialization.Serialize(origin))).IsTrue();
            var resolved = store.Read(view => ResourceRetirementConservationProvenance.Resolve(view,
                proof.Floor!, origin.TargetOwner, options.MaxFrameBytes, P.Two, cancellationToken: token));
            await EqualAsync(Floor(origin), resolved);
        }
        var missing = Assert.ThrowsExactly<KeyLoadException>(() => store.Read(view =>
            ResourceRetirementConservationProvenance.Resolve(view, proof.Floor!, neverIssuedOwner,
                options.MaxFrameBytes, P.Two, cancellationToken: token)));
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        var actualOrigins = store.Read(view => view.Scan(ResourceRetirementConservationOriginKeys.Prefix(), P.Two));
        await Assert.That(actualOrigins.HasMore).IsFalse();
        await Assert.That(actualOrigins.Records.Length).IsEqualTo(origins.Length);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(store).SequenceEqual(rows, StringComparer.Ordinal)).IsTrue();
    }
}
