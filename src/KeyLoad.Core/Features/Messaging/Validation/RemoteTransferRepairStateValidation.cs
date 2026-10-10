namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferRepairStateValidation
{
    internal static RemoteTransferRepairState? Initial(int? ceiling)
        => ceiling is { } cap ? new(cap, RemoteTransferRepairProtocol.InitialGeneration,
            RemoteTransferRepairProtocol.InitialGeneration, []) : null;

    internal static void Require(RemoteTransferRepairState? state)
    {
        if (state is null)
        { return; }
        var first = RemoteTransferRepairProtocol.InitialGeneration;
        if (state.Ceiling < first || state.AcceptPolicyGeneration < first || state.CompleteGeneration < first
            || state.History.IsDefault || state.History.Length >= state.Ceiling
            || state.History.Length != checked(state.AcceptPolicyGeneration - first + state.CompleteGeneration - first))
        { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferRepairProtocol.Invalid); }
        var policy = first;
        var complete = first;
        foreach (var reference in state.History)
        {
            if (reference is null || !Enum.IsDefined(reference.Stage) || reference.PolicyGeneration != policy
                || reference.CompleteGeneration != complete || reference.CapacityGeneration < first
                || reference.FailedCommandId == Guid.Empty || !RemoteTransferDependencyShape.Digest(reference.OutcomeDigest)
                || !RemoteTransferDependencyShape.Digest(reference.Fingerprint) || string.IsNullOrEmpty(reference.WitnessToken))
            { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferRepairProtocol.Invalid); }
            if (reference.Stage == QueueTransferRepairStage.Accept)
            { policy = checked(policy + first); }
            else
            { complete = checked(complete + first); }
        }
    }
}
