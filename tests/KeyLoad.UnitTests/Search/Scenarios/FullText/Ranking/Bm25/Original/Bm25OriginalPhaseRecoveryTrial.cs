using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25OriginalPhaseRecoveryTrial
{
    internal static async Task RunAsync(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var original = flow.Capture(null);
        var originalResult = flow.Publish(original);
        await flow.RequireHealthyAsync(original, originalResult);
        var originalEntry = NativeSerialization.Serialize(flow.Owner.Entry(original.Id));
        await RequireColdReplayAsync(flow, original, originalResult, originalEntry);
        flow.RevokeAdministrator();
        await Assert.That(flow.Administrator.PolicyEpoch).IsEqualTo(2L);
        await flow.RequireDeniedAsync(original, issuerMustDeny: true);
        flow.RepairAdministrator();
        await Assert.That(flow.Administrator.PolicyEpoch).IsEqualTo(3L);
        await flow.RequireDeniedAsync(original, issuerMustDeny: false);
        var repairedAdmin = flow.Capture(original.Id);
        var repairedResult = flow.Publish(repairedAdmin);
        await flow.RequireHealthyAsync(repairedAdmin, repairedResult);
        flow.RevokeSubject();
        await Assert.That(flow.Reader.PolicyEpoch).IsEqualTo(2L);
        await flow.RequireDeniedAsync(repairedAdmin, issuerMustDeny: true);
        flow.RepairSubject();
        await Assert.That(flow.Reader.PolicyEpoch).IsEqualTo(3L);
        await flow.RequireDeniedAsync(repairedAdmin, issuerMustDeny: true);
        var repairedSubject = flow.Capture(repairedAdmin.Id);
        var subjectResult = flow.Publish(repairedSubject);
        await flow.RequireHealthyAsync(repairedSubject, subjectResult);
        var subjectEntry = NativeSerialization.Serialize(flow.Owner.Entry(repairedSubject.Id));
        await RequireColdReplayAsync(flow, repairedSubject, subjectResult, subjectEntry);
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(flow.Owner, original, originalEntry);
        var healthy = flow.Capture(repairedSubject.Id);
        await flow.RequireHealthyAsync(healthy, flow.Publish(healthy));
    }

    private static async Task RequireColdReplayAsync(Bm25OriginalPhaseRecoveryFlow flow,
        Command<DatabaseCommand> original, Result<object> expected, byte[] entry)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        flow.Owner.Reopen();
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(flow.Owner, original, entry);
        var replay = flow.Publish(flow.Reissue(original));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(expected, replay);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(expected.Get<CommitReceipt>()),
            NativeSerialization.Serialize(replay.Get<CommitReceipt>()));
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(flow.Owner, original, entry);
        await flow.RequireHealthyAsync(original, replay);
    }
}
