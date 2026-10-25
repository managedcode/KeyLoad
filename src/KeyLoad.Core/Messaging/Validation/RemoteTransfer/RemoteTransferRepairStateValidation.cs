namespace KeyLoad.Core.Messaging;

internal static class RemoteTransferRepairStateValidation
{
    internal static global::ManagedCode.Communication.Result<RemoteTransferRepairState?> Initial(int? ceiling)
        => (ceiling is { } cap ? new RemoteTransferRepairState(cap, RemoteTransferRepairProtocol.InitialGeneration,
            RemoteTransferRepairProtocol.InitialGeneration, []) : null) is { } outcomeValue1 ? global::ManagedCode.Communication.Result<RemoteTransferRepairState?>.Succeed(outcomeValue1) : global::ManagedCode.Communication.Result<RemoteTransferRepairState?>.FailNotFound(nameof(Initial));

    internal static void Require(RemoteTransferRepairState? state)
    {
        if (state is null)
        {
            return;
        }
        var first = RemoteTransferRepairProtocol.InitialGeneration;
        if (state.Ceiling < first || state.AcceptPolicyGeneration < first || state.CompleteGeneration < first
            || state.History.IsDefault || state.History.Length >= state.Ceiling
            || state.AcceptPolicyGeneration - first > state.History.Length
            || state.CompleteGeneration - first != state.History.Length - (state.AcceptPolicyGeneration - first))
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteTransferRepairProtocol.Invalid);
        }
        var policy = first;
        var complete = first;
        foreach (var reference in state.History)
        {
            if (reference is null || !Enum.IsDefined(reference.Stage) || reference.PolicyGeneration != policy
                || reference.CompleteGeneration != complete || reference.CapacityGeneration < first
                || reference.FailedCommandId == Guid.Empty || !RemoteTransferDependencyShape.Digest(reference.OutcomeDigest)
                || !RemoteTransferDependencyShape.Digest(reference.Fingerprint) || string.IsNullOrEmpty(reference.WitnessToken))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteTransferRepairProtocol.Invalid);
            }
            if (reference.Stage == QueueTransferRepairStage.Accept)
            {
                policy = checked(policy + first);
            }
            else
            {
                complete = checked(complete + first);
            }
        }
        if (policy != state.AcceptPolicyGeneration || complete != state.CompleteGeneration)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteTransferRepairProtocol.Invalid);
        }
    }
}
