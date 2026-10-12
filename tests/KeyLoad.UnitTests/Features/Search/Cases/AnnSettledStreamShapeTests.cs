using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSettledStreamShapeTests
{
    private const int FirstProgressSequence = 2;
    private const int FirstOffset = 0;
    private const int Step = 1;
    private const int TwoSteps = 2;
    private const int MultipleReplayPages = 2;
    private const int SkippedNativeIndexFrame = 3;
    private const int SkippedCheckpointFrame = 6;

    [Test]
    public async Task RetainedOriginalHasExactlyOneCompletedProgressAndRejectsDuplicateWithoutChangingTerminalState()
    {
        var id = Guid.NewGuid();
        var frames = await NativeAnnSettledProgressFrames.CreateAsync(id,
            [AnnMaintenancePhase.Completed, AnnMaintenancePhase.Completed]);
        var shape = new AnnMaintenanceStreamShape(id);
        await Assert.That(shape.Completed).IsFalse();
        shape.Admit(frames[FirstOffset], FirstProgressSequence);
        await Assert.That(shape.Completed).IsTrue();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => shape.Admit(frames[Step], FirstProgressSequence + Step));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(shape.Completed).IsTrue();
    }

    [Test]
    public async Task DirectCompletedReplayStillRejectsAnotherRequestIdentityAndRetainsFreshShape()
    {
        var id = Guid.NewGuid();
        var wrong = await NativeAnnSettledProgressFrames.CreateAsync(Guid.NewGuid(), [AnnMaintenancePhase.Completed]);
        var original = await NativeAnnSettledProgressFrames.CreateAsync(id, [AnnMaintenancePhase.Completed]);
        var shape = new AnnMaintenanceStreamShape(id);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => shape.Admit(wrong[FirstOffset], FirstProgressSequence));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(shape.Completed).IsFalse();
        shape.Admit(original[FirstOffset], FirstProgressSequence);
        await Assert.That(shape.Completed).IsTrue();
    }

    [Test]
    [Arguments(FirstOffset)]
    [Arguments(MultipleReplayPages)]
    public async Task FreshNativeGrammarPreservesZeroOrMultipleReplayPages(int replayPages)
    {
        var id = Guid.NewGuid();
        var phases = new List<AnnMaintenancePhase> { AnnMaintenancePhase.Configure, AnnMaintenancePhase.Capture };
        for (var page = FirstOffset; page < replayPages; page++)
        { phases.Add(AnnMaintenancePhase.Replay); }
        phases.AddRange([AnnMaintenancePhase.Verify, AnnMaintenancePhase.NativeIndex,
            AnnMaintenancePhase.Publish, AnnMaintenancePhase.Checkpoint, AnnMaintenancePhase.Completed]);
        var frames = await NativeAnnSettledProgressFrames.CreateAsync(id, phases.ToArray());
        var shape = new AnnMaintenanceStreamShape(id);
        for (var index = FirstOffset; index < frames.Length; index++)
        {
            await Assert.That(shape.Completed).IsFalse();
            shape.Admit(frames[index], FirstProgressSequence + index);
        }
        await Assert.That(shape.Completed).IsTrue();
    }

    [Test]
    public async Task PartialFreshConfigureCannotMasqueradeAsSettledReplayAndRefusalPreservesContinuation()
    {
        var id = Guid.NewGuid();
        var frames = await NativeAnnSettledProgressFrames.CreateAsync(id,
            [AnnMaintenancePhase.Configure, AnnMaintenancePhase.Completed, AnnMaintenancePhase.Capture]);
        var shape = new AnnMaintenanceStreamShape(id);
        shape.Admit(frames[FirstOffset], FirstProgressSequence);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => shape.Admit(frames[Step], FirstProgressSequence + Step));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(shape.Completed).IsFalse();
        shape.Admit(frames[TwoSteps], FirstProgressSequence + TwoSteps);
        await Assert.That(shape.Completed).IsFalse();
    }

    [Test]
    public async Task FreshPublicationCannotSkipNativeIndexOrCheckpoint()
    {
        var id = Guid.NewGuid();
        var frames = await NativeAnnSettledProgressFrames.CreateAsync(id,
            [AnnMaintenancePhase.Configure, AnnMaintenancePhase.Capture, AnnMaintenancePhase.Verify,
             AnnMaintenancePhase.Publish, AnnMaintenancePhase.NativeIndex, AnnMaintenancePhase.Publish,
             AnnMaintenancePhase.Completed, AnnMaintenancePhase.Checkpoint, AnnMaintenancePhase.Completed]);
        var shape = new AnnMaintenanceStreamShape(id);
        for (var index = FirstOffset; index < frames.Length; index++)
        {
            if (index is SkippedNativeIndexFrame or SkippedCheckpointFrame)
            {
                var error = Assert.ThrowsExactly<KeyLoadException>(() => shape.Admit(frames[index], FirstProgressSequence + index));
                await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
                await Assert.That(shape.Completed).IsFalse();
            }
            else
            { shape.Admit(frames[index], FirstProgressSequence + index); }
        }
        await Assert.That(shape.Completed).IsTrue();
    }
}
