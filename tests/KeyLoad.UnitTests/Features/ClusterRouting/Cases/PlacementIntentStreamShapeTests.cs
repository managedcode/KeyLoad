using KeyLoad.Clustering;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PlacementIntentStreamShapeTests
{
    private const int First = 0;
    private const int Second = 1;
    private const int FirstProgressSequence = 2;
    private const int SecondProgressSequence = 3;
    private const int TwoMoves = 2;
    private const int ThreeMoves = 3;
    private const int CanonicalMoveLimit = 64;
    private const int BeyondCanonicalMoveLimit = 65;
    private const long OriginalRevision = 4;
    private const long FirstObservedRevision = 10;
    private const long NextObservedRevision = 12;

    [Test]
    public async Task ResumedOriginalPrefixAcceptsOnlyItsRemainingOrderedRetiredReceipts()
    {
        var requestId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var first = new PlacementIntentProgressV1(intentId, FirstObservedRevision, TwoMoves, ThreeMoves,
            Guid.NewGuid(), PartitionMovePhase.Retired);
        var next = first with
        {
            Revision = NextObservedRevision,
            CompletedMoves = ThreeMoves,
            OriginalMoveId = Guid.NewGuid()
        };
        var frames = await NativePlacementProgressFrames.CreateAsync(requestId, first, next);
        var shape = new PlacementIntentStreamShape(requestId, intentId, OriginalRevision);
        await Assert.That(shape.Completed).IsTrue();
        shape.Admit(frames[First], FirstProgressSequence);
        await Assert.That(shape.Completed).IsFalse();
        shape.Admit(frames[Second], SecondProgressSequence);
        await Assert.That(shape.Completed).IsTrue();
    }

    [Test]
    [Arguments(PlacementIntentProgressFault.OtherRequest)]
    [Arguments(PlacementIntentProgressFault.OtherIntent)]
    [Arguments(PlacementIntentProgressFault.RepeatedMove)]
    [Arguments(PlacementIntentProgressFault.EmptyMove)]
    [Arguments(PlacementIntentProgressFault.RepeatedRevision)]
    [Arguments(PlacementIntentProgressFault.RepeatedCompletedCount)]
    [Arguments(PlacementIntentProgressFault.ChangedTotal)]
    [Arguments(PlacementIntentProgressFault.NonRetiredPhase)]
    [Arguments(PlacementIntentProgressFault.MixedPayload)]
    [Arguments(PlacementIntentProgressFault.WrongSequence)]
    [Arguments(PlacementIntentProgressFault.WrongMessage)]
    public async Task RejectedFramePreservesTheOriginalPrefixAndItsHealthyContinuation(PlacementIntentProgressFault fault)
    {
        var requestId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var first = new PlacementIntentProgressV1(intentId, FirstObservedRevision, Second, TwoMoves,
            Guid.NewGuid(), PartitionMovePhase.Retired);
        var next = first with
        {
            Revision = NextObservedRevision,
            CompletedMoves = TwoMoves,
            OriginalMoveId = Guid.NewGuid()
        };
        var frames = await NativePlacementProgressFrames.CreateAsync(requestId, first, next);
        var candidate = next;
        candidate = fault switch
        {
            PlacementIntentProgressFault.OtherIntent => candidate with { IntentId = Guid.NewGuid() },
            PlacementIntentProgressFault.RepeatedMove => candidate with { OriginalMoveId = first.OriginalMoveId },
            PlacementIntentProgressFault.EmptyMove => candidate with { OriginalMoveId = Guid.Empty },
            PlacementIntentProgressFault.RepeatedRevision => candidate with { Revision = first.Revision },
            PlacementIntentProgressFault.RepeatedCompletedCount => candidate with { CompletedMoves = first.CompletedMoves },
            PlacementIntentProgressFault.ChangedTotal => candidate with { TotalMoves = ThreeMoves },
            PlacementIntentProgressFault.NonRetiredPhase => candidate with { ActualPhase = PartitionMovePhase.Published },
            _ => candidate
        };
        var progress = new GrainRequestProgress(fault == PlacementIntentProgressFault.OtherRequest ? Guid.NewGuid() : requestId)
        {
            PlacementIntent = candidate,
            MovePhase = fault == PlacementIntentProgressFault.MixedPayload ? PartitionMovePhase.Published : null
        };
        var rejected = frames[Second] with
        {
            ProgressResult = Result<GrainRequestProgress>.Succeed(progress),
            Sequence = fault == PlacementIntentProgressFault.WrongSequence ? FirstProgressSequence : SecondProgressSequence,
            Message = fault == PlacementIntentProgressFault.WrongMessage ? "unexpected progress" : frames[Second].Message
        };
        var shape = new PlacementIntentStreamShape(requestId, intentId, OriginalRevision);
        shape.Admit(frames[First], FirstProgressSequence);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => shape.Admit(rejected, SecondProgressSequence));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(error.Message).IsEqualTo(GrainRoutingProtocol.InvalidRequest);
        await Assert.That(shape.Completed).IsFalse();
        shape.Admit(frames[Second], SecondProgressSequence);
        await Assert.That(shape.Completed).IsTrue();
    }

    [Test]
    public async Task FirstProgressCannotReuseTheSignedExpectationRevisionOrAnEmptyIntent()
    {
        var requestId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var actual = new PlacementIntentProgressV1(intentId, OriginalRevision, Second, Second,
            Guid.NewGuid(), PartitionMovePhase.Retired);
        var frames = await NativePlacementProgressFrames.CreateAsync(requestId, actual);
        var shape = new PlacementIntentStreamShape(requestId, intentId, OriginalRevision);
        var stale = Assert.ThrowsExactly<KeyLoadException>(() => shape.Admit(frames[First], FirstProgressSequence));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(shape.Completed).IsTrue();
        var validFrames = await NativePlacementProgressFrames.CreateAsync(requestId, actual with
        {
            Revision = FirstObservedRevision
        });
        var invalidScope = new PlacementIntentStreamShape(requestId, Guid.Empty, OriginalRevision);
        var missing = Assert.ThrowsExactly<KeyLoadException>(() => invalidScope.Admit(validFrames[First], FirstProgressSequence));
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(invalidScope.Completed).IsTrue();
        shape.Admit(validFrames[First], FirstProgressSequence);
        await Assert.That(shape.Completed).IsTrue();
    }

    [Test]
    public async Task FirstObservationRejectsSixtyFiveMovesThenAcceptsTheCanonicalSixtyFourMoveSuffix()
    {
        var requestId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var over = new PlacementIntentProgressV1(intentId, FirstObservedRevision, CanonicalMoveLimit,
            BeyondCanonicalMoveLimit, Guid.NewGuid(), PartitionMovePhase.Retired);
        var invalidFrames = await NativePlacementProgressFrames.CreateAsync(requestId, over);
        var validFrames = await NativePlacementProgressFrames.CreateAsync(requestId, over with
        {
            TotalMoves = CanonicalMoveLimit
        });
        var shape = new PlacementIntentStreamShape(requestId, intentId, OriginalRevision);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => shape.Admit(invalidFrames[First], FirstProgressSequence));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(error.Message).IsEqualTo(GrainRoutingProtocol.InvalidRequest);
        shape.Admit(validFrames[First], FirstProgressSequence);
        await Assert.That(shape.Completed).IsTrue();
    }
}
