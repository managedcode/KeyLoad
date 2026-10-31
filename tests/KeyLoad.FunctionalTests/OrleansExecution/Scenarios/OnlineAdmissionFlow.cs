using KeyLoad.FunctionalTests.Search;

namespace KeyLoad.FunctionalTests.OrleansExecution;

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
        partial.Admit(frames[ProgressAdmissionSupport.Started]);
        partial.Admit(frames[ProgressAdmissionSupport.First]);
        await ProgressAdmissionSupport.RefusedAsync(() => partial.Admit(
            ProgressAdmissionSupport.Terminal(ProgressAdmissionSupport.SecondSequence)), ErrorCode.OwnershipLost);
        await ProgressAdmissionSupport.CompleteAsync(partial, frames.Skip(ProgressAdmissionSupport.FirstSequence));
        var wrong = ProgressAdmissionSupport.Admission(fixture, id, OperationKind.Batch, request);
        wrong.Admit(frames[ProgressAdmissionSupport.Started]);
        await ProgressAdmissionSupport.RefusedAsync(() => wrong.Admit(
            frames[ProgressAdmissionSupport.First]), ErrorCode.OwnershipLost);
    }
}
