namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorAdmittedCall
{
    private EventVectorAdmittedCall(EventVectorMacCheckedCall original) => Original = original;
    internal EventVectorMacCheckedCall Original { get; }

    internal static EventVectorAdmittedCall Admit(EventVectorMacCheckedCall original,
        EventVectorRemoteCallValidation authority, EventVectorReplayCache replay,
        CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(replay);
        authority.Require(original.Call, originalCancellationToken);
        if (!replay.TryUse(original.Call.Nonce, original.Call.ExpiresAt))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        authority.Require(original.Call, originalCancellationToken);
        return new(original);
    }

    internal static EventVectorAdmittedCall AdmitLocal(EventVectorMacCheckedCall original,
        EventVectorRemoteCallValidation authority, OrleansNode node,
        EventVectorReplayCache replay, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(node);
        EventVectorLocalCallAuthority.Require(original.Call, authority, node, originalCancellationToken);
        return Admit(original, authority, replay, originalCancellationToken);
    }
}
