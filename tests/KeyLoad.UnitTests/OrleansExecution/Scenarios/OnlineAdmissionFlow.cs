using KeyLoad.UnitTests.Search;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class OnlineAdmissionFlow
{
    internal static async Task GrammarAsync(RequestCqrsHostedRuntimeFixture fixture)
    {
        var id = Guid.NewGuid();
        var request = ProgressAdmissionSupport.Online(fixture);
        var frames = await OnlineTextProgressFrames.CreateAsync(id, OnlineTextProgressFrames.Grammar);
        var full = ProgressAdmissionSupport.Admission(fixture, id, OperationKind.MaintainOnlineTextIndex, request);
        await ProgressAdmissionSupport.CompleteAsync(full, frames);
        var replay = ProgressAdmissionSupport.Admission(fixture, id, OperationKind.MaintainOnlineTextIndex, request);
        await ProgressAdmissionSupport.CompleteAsync(replay, await OnlineTextProgressFrames.CreateAsync(id, []));
        var partial = ProgressAdmissionSupport.Admission(fixture, id, OperationKind.MaintainOnlineTextIndex, request);
        partial.Admit(frames[ProgressAdmissionSupport.Started], CancellationToken.None);
        partial.Admit(frames[ProgressAdmissionSupport.First], CancellationToken.None);
        await ProgressAdmissionSupport.RefusedAsync(() => partial.Admit(
            ProgressAdmissionSupport.Terminal(ProgressAdmissionSupport.SecondSequence), CancellationToken.None), ErrorCode.OwnershipLost);
        await ProgressAdmissionSupport.CompleteAsync(partial, frames.Skip(ProgressAdmissionSupport.FirstSequence));
        var wrong = ProgressAdmissionSupport.Admission(fixture, id, OperationKind.Batch, request);
        wrong.Admit(frames[ProgressAdmissionSupport.Started], CancellationToken.None);
        await ProgressAdmissionSupport.RefusedAsync(() => wrong.Admit(
            frames[ProgressAdmissionSupport.First], CancellationToken.None), ErrorCode.OwnershipLost);
    }

    internal static async Task CancelledAsync(RequestCqrsHostedRuntimeFixture fixture)
    {
        var id = Guid.NewGuid();
        var frames = await OnlineTextProgressFrames.CreateAsync(id, OnlineTextProgressFrames.Grammar);
        var admission = ProgressAdmissionSupport.Admission(fixture, id,
            OperationKind.MaintainOnlineTextIndex, ProgressAdmissionSupport.Online(fixture));
        admission.Admit(frames[ProgressAdmissionSupport.Started], CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            admission.Admit(frames[ProgressAdmissionSupport.First], cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await ProgressAdmissionSupport.CompleteAsync(admission, frames.Skip(ProgressAdmissionSupport.First));
    }
}
