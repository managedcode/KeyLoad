using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationOutbox
{
    private const string Outbox = "outbox";
    private const string Head = "outbox-head";
    private const long FirstSequence = 1;
    private const long SecondSequence = 2;
    private const long PublicSequence = 3;
    private const long HealthySequence = 4;
    private const int FirstOrdinal = 0;
    private const int SecondOrdinal = 1;

    internal static async Task CurrentAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationState state,
        SafeProjectorOperationCommand? patch = null, SafeProjectorOperationCommand? healthy = null)
    {
        var expected = Expected(owner, state, patch, healthy);
        var page = owner.Store.Read(view => view.Scan(KeySpace.Partition(Outbox, owner.Source.Partition), owner.Database.Limits.MaxScanRecords));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(expected.Length);
        foreach (var (row, literal) in page.Records.Zip(expected))
        {
            await Assert.That(row.Key.Span.SequenceEqual(KeySpace.Partition(Outbox, owner.Source.Partition, literal.Sequence))).IsTrue();
            var actual = NativeSerialization.Deserialize<OutboxEntry>(row.Value.Span);
            await SafeProjectorOperationSeed.EqualAsync(actual, literal);
            await Assert.That(actual.Receipt.CompositionReferences).IsEmpty();
        }
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<OutboxHead>(
            KeySpace.Partition(Head, owner.Source.Partition))), new OutboxHead(expected.Length, FirstSequence,
                expected.Length, page.Records.Sum(row => row.Value.Length)));
    }

    private static OutboxEntry[] Expected(SafeProjectorOperationDatabase owner, SafeProjectorOperationState state,
        SafeProjectorOperationCommand? patch, SafeProjectorOperationCommand? healthy)
    {
        var firstRef = new EntityRef(owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First);
        var secondRef = new EntityRef(owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.Second);
        var first = new DocumentRecord(firstRef, SafeProjectorOperationProtocol.InitialRevision,
            SafeProjectorOperationProtocol.InitialJson, new RowAccess(), state.Seed.Operation.EvaluatedAt);
        var second = new DocumentRecord(secondRef, SafeProjectorOperationProtocol.InitialRevision,
            SafeProjectorOperationProtocol.SecondJson, new RowAccess(), state.Seed.Operation.EvaluatedAt);
        List<OutboxEntry> rows =
        [
            new(FirstSequence, FirstOrdinal, state.Seed.ExpectedToken, state.Seed.Operation.EvaluatedAt,
                new PutDocument(SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First,
                    SafeProjectorOperationProtocol.InitialJson, SafeProjectorOperationProtocol.AbsentRevision),
                new(SafeProjectorOperationProtocol.PutKind, SafeProjectorOperationProtocol.Collection,
                    SafeProjectorOperationProtocol.First, SafeProjectorOperationProtocol.InitialRevision), null, first),
            new(SecondSequence, SecondOrdinal, state.Seed.ExpectedToken, state.Seed.Operation.EvaluatedAt,
                new PutDocument(SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.Second,
                    SafeProjectorOperationProtocol.SecondJson, SafeProjectorOperationProtocol.AbsentRevision),
                new(SafeProjectorOperationProtocol.PutKind, SafeProjectorOperationProtocol.Collection,
                    SafeProjectorOperationProtocol.Second, SafeProjectorOperationProtocol.InitialRevision), null, second)
        ];
        var updated = patch is null ? null : new DocumentRecord(firstRef, SafeProjectorOperationProtocol.UpdatedRevision,
            SafeProjectorOperationProtocol.UpdatedJson, new RowAccess(), patch.Operation.EvaluatedAt);
        if (patch is not null)
        {
            rows.Add(new(PublicSequence, FirstOrdinal, patch.ExpectedToken, patch.Operation.EvaluatedAt,
                PublicPatch(), new(SafeProjectorOperationProtocol.PatchKind, SafeProjectorOperationProtocol.Collection,
                    SafeProjectorOperationProtocol.First, SafeProjectorOperationProtocol.UpdatedRevision), first, updated));
        }
        if (healthy is not null)
        {
            rows.Add(new(HealthySequence, FirstOrdinal, healthy.ExpectedToken, healthy.Operation.EvaluatedAt,
                HealthyPatch(), new(SafeProjectorOperationProtocol.PatchKind, SafeProjectorOperationProtocol.Collection,
                    SafeProjectorOperationProtocol.First, SafeProjectorOperationProtocol.HealthyRevision), updated,
                new DocumentRecord(firstRef, SafeProjectorOperationProtocol.HealthyRevision, SafeProjectorOperationProtocol.HealthyJson,
                    new RowAccess(), healthy.Operation.EvaluatedAt)));
        }
        return [.. rows];
    }

    private static PatchDocument PublicPatch() => new(SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First,
        [new(SafeProjectorOperationProtocol.StatusPath, PatchKind.Set, UpdatedStatusJson)], SafeProjectorOperationProtocol.InitialRevision);

    private static PatchDocument HealthyPatch() => new(SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First,
    [
        new(SafeProjectorOperationProtocol.StatusPath, PatchKind.Set, HealthyStatusJson),
        new(SafeProjectorOperationProtocol.LiteralPath, PatchKind.Set, CanaryJson)
    ], SafeProjectorOperationProtocol.UpdatedRevision);

    private const string UpdatedStatusJson = "\"updated\"";
    private const string HealthyStatusJson = "\"healthy\"";
    private const string CanaryJson = "\"SAFE-PROJECTOR-PRIVATE-CANARY\"";
}
