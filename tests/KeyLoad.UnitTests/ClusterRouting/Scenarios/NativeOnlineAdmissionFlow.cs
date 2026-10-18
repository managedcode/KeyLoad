using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeOnlineAdmissionFlow
{
    internal static async Task GrammarAsync(RequestCqrsHostedRuntimeFixture fixture)
    {
        var id = Guid.NewGuid();
        var request = NativeProgressAdmissionSupport.Online(fixture);
        var frames = await NativeOnlineTextProgressFrames.CreateAsync(id, NativeOnlineTextProgressFrames.Grammar);
        var full = NativeProgressAdmissionSupport.Admission(fixture, id, OperationKind.MaintainOnlineTextIndex, request);
        await NativeProgressAdmissionSupport.CompleteAsync(full, frames);
        var replay = NativeProgressAdmissionSupport.Admission(fixture, id, OperationKind.MaintainOnlineTextIndex, request);
        await NativeProgressAdmissionSupport.CompleteAsync(replay, await NativeOnlineTextProgressFrames.CreateAsync(id, []));
        var partial = NativeProgressAdmissionSupport.Admission(fixture, id, OperationKind.MaintainOnlineTextIndex, request);
        partial.Admit(frames[NativeProgressAdmissionSupport.Started], CancellationToken.None);
        partial.Admit(frames[NativeProgressAdmissionSupport.First], CancellationToken.None);
        await NativeProgressAdmissionSupport.RefusedAsync(() => partial.Admit(
            NativeProgressAdmissionSupport.Terminal(NativeProgressAdmissionSupport.SecondSequence), CancellationToken.None), ErrorCode.OwnershipLost);
        await NativeProgressAdmissionSupport.CompleteAsync(partial, frames.Skip(NativeProgressAdmissionSupport.FirstSequence));
        var wrong = NativeProgressAdmissionSupport.Admission(fixture, id, OperationKind.Batch, request);
        wrong.Admit(frames[NativeProgressAdmissionSupport.Started], CancellationToken.None);
        await NativeProgressAdmissionSupport.RefusedAsync(() => wrong.Admit(
            frames[NativeProgressAdmissionSupport.First], CancellationToken.None), ErrorCode.OwnershipLost);
    }

    internal static async Task CancelledAsync(RequestCqrsHostedRuntimeFixture fixture)
    {
        var id = Guid.NewGuid();
        var frames = await NativeOnlineTextProgressFrames.CreateAsync(id, NativeOnlineTextProgressFrames.Grammar);
        var admission = NativeProgressAdmissionSupport.Admission(fixture, id,
            OperationKind.MaintainOnlineTextIndex, NativeProgressAdmissionSupport.Online(fixture));
        admission.Admit(frames[NativeProgressAdmissionSupport.Started], CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            admission.Admit(frames[NativeProgressAdmissionSupport.First], cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await NativeProgressAdmissionSupport.CompleteAsync(admission, frames.Skip(NativeProgressAdmissionSupport.First));
    }
}
