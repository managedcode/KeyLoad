using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;
using static KeyLoad.UnitTests.Search.OnlineTextProgressFrames;

namespace KeyLoad.UnitTests.Search;

internal sealed class OnlineTextMaintenanceStreamShapeTests
{
    private const int StartedAndTerminal = 2;

    [Test]
    public async Task RetainedOriginalHasNoProgressAndEveryFreshPrefixRequiresTheRemainingProductionPhases()
    {
        var id = Guid.NewGuid();
        var retained = await CreateAsync(id, []);
        var shape = new OnlineTextMaintenanceStreamShape(id);
        await Assert.That(retained.Length).IsEqualTo(StartedAndTerminal);
        await Assert.That(retained[First].Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(retained[First].Sequence).IsEqualTo(StartedSequence);
        await Assert.That(retained[Step].Kind).IsEqualTo(CqrsStreamChunkKind.Completed);
        await Assert.That(retained[Step].Sequence).IsEqualTo(StartedAndTerminal);
        await Assert.That(shape.Completed).IsTrue();
        var all = await CreateAsync(id, Grammar);
        var frames = all.Where(frame => frame.Kind == CqrsStreamChunkKind.Progress).ToArray();
        await Assert.That(frames.Length).IsEqualTo(Grammar.Length);
        for (var index = First; index < frames.Length; index++)
        {
            await Assert.That(frames[index].Sequence).IsEqualTo(FirstProgressSequence + index);
            await Assert.That(frames[index].ProgressResult!.Value.Value!.OnlineTextPhase).IsEqualTo(Grammar[index]);
            shape.Admit(frames[index], FirstProgressSequence + index);
            await Assert.That(shape.Completed).IsEqualTo(index == frames.Length - Step);
        }
        await Assert.That(all.Last().Sequence).IsEqualTo(Grammar.Length + StartedAndTerminal);
        await Assert.That(shape.Completed).IsTrue();
    }

    [Test]
    public async Task SkippedAndRepeatedPhasesRejectWithoutConsumingTheOriginalProductionContinuation()
    {
        var id = Guid.NewGuid();
        var frames = (await CreateAsync(id, Grammar)).Where(frame => frame.Kind == CqrsStreamChunkKind.Progress).ToArray();
        for (var next = First; next < frames.Length; next++)
        {
            var skipped = frames[(next + Step) % frames.Length] with
            {
                Sequence = frames[next].Sequence
            };
            await OnlineTextProgressShapeRefusals.ThenCompleteAsync(id, frames, next, skipped);
            var previous = next == First ? frames.Length - Step : next - Step;
            var repeated = frames[previous] with
            {
                Sequence = frames[next].Sequence
            };
            await OnlineTextProgressShapeRefusals.ThenCompleteAsync(id, frames, next, repeated);
        }
        var shape = new OnlineTextMaintenanceStreamShape(id);
        for (var index = First; index < frames.Length; index++)
        {
            shape.Admit(frames[index], FirstProgressSequence + index);
        }
        var duplicate = frames.Last() with
        {
            Sequence = Grammar.Length + FirstProgressSequence
        };
        var error = Assert.ThrowsExactly<KeyLoadException>(() => shape.Admit(duplicate, Grammar.Length + FirstProgressSequence));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(shape.Completed).IsTrue();
    }

    [Test]
    public async Task CorruptEnvelopeWrongRequestAndEveryAlienUnionPayloadPreserveEachOriginalPrefix()
    {
        var id = Guid.NewGuid();
        var frames = (await CreateAsync(id, Grammar)).Where(frame => frame.Kind == CqrsStreamChunkKind.Progress).ToArray();
        for (var next = First; next < frames.Length; next++)
        {
            foreach (var rejected in OnlineTextProgressShapeRefusals.Corruptions(frames[next]))
            {
                await OnlineTextProgressShapeRefusals.ThenCompleteAsync(id, frames, next, rejected);
            }
        }
    }
}
