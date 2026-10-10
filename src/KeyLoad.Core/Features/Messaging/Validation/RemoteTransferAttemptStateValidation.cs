
namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAttemptStateValidation
{
    internal static RemoteTransferAcceptAttemptState? Initial(int? ceiling)
        => ceiling is { } cap ? new(cap, RemoteTransferAttemptProtocol.FirstGeneration, []) : null;

    internal static void Require(RemoteTransferAcceptAttemptState? state)
    {
        if (state is null)
        { return; }
        if (state.Ceiling < RemoteTransferAttemptProtocol.FirstGeneration
            || state.Generation < RemoteTransferAttemptProtocol.FirstGeneration || state.Generation > state.Ceiling
            || state.History.IsDefault || state.History.Length != state.Generation - RemoteTransferAttemptProtocol.FirstGeneration)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferAttemptProtocol.Invalid); }
        var generation = RemoteTransferAttemptProtocol.FirstGeneration;
        foreach (var item in state.History)
        {
            if (item is null || item.Generation != generation++ || item.AcceptCommandId == Guid.Empty
                || string.IsNullOrEmpty(item.OutcomeDigest) || string.IsNullOrEmpty(item.WitnessToken))
            { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferAttemptProtocol.Invalid); }
        }
    }
}
