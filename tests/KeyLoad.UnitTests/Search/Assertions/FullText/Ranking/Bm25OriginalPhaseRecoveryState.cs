namespace KeyLoad.UnitTests.Features.Search;

internal sealed record Bm25OriginalPhaseRecoveryState(Guid NodeId, Guid Incarnation, long Position,
    long Applied, long LastIndex, long CommittedIndex, long Term, byte[] Rows)
{
    internal static Bm25OriginalPhaseRecoveryState Read(Bm25OriginalPhaseRecoveryOwner owner)
    {
        var work = owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        var image = owner.Store.Read(view =>
        {
            var rows = new List<string>();
            var scan = view.VisitRange([], owner.Source.Database.Limits.MaxScanRecords,
                (key, value) =>
                {
                    rows.Add(Convert.ToHexString(key) + ":" + Convert.ToHexString(value));
                    return true;
                }, observer: work.ChargeBytes, cancellationToken: Bm25OriginalPhaseRecoveryFlow.Token);
            if (scan.HasMore)
            {
                throw new InvalidOperationException("The complete original canonical image exceeded the native record bound.");
            }
            return (Position: owner.Store.Position, Rows: NativeSerialization.Serialize(rows.ToArray()));
        });
        var journal = owner.JournalCut();
        return new(owner.Store.Identity.NodeId, owner.Store.Identity.Incarnation, image.Position,
            owner.Database.LastApplied, journal.LastIndex, journal.CommittedIndex, journal.Term, image.Rows);
    }

    internal async Task RequireAsync(Bm25OriginalPhaseRecoveryState actual)
    {
        await Assert.That(actual.NodeId).IsEqualTo(NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(Incarnation);
        await Assert.That(actual.Position).IsEqualTo(Position);
        await Assert.That(actual.Applied).IsEqualTo(Applied);
        await Assert.That(actual.LastIndex).IsEqualTo(LastIndex);
        await Assert.That(actual.CommittedIndex).IsEqualTo(CommittedIndex);
        await Assert.That(actual.Term).IsEqualTo(Term);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(Rows, actual.Rows);
    }

    internal static async Task RequireRetainedAsync(Bm25OriginalPhaseRecoveryOwner owner,
        ReplicatedOperation original, byte[] originalEntry)
    {
        var actual = owner.Entry(original.Id);
        await Assert.That(actual.Operation).IsNotNull();
        await Assert.That(actual.Operation!.Kind).IsEqualTo(OperationKind.Bm25CorpusPublicationPhase);
        await Assert.That(actual.Operation.Id).IsEqualTo(original.Id);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(original.NativePayload.ToArray(),
            actual.Operation.NativePayload.ToArray());
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(originalEntry, NativeSerialization.Serialize(actual));
    }
}
