using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class RemoteTransferPendingCursorSelection
{
    private static readonly byte[] Prefix = KeyCodec.Encode(RemoteTransferProtocol.IntentSpace);

    internal static RemoteTransferPendingCursor Match(StoreIdentity identity, RemoteTransferPendingCursor? supplied)
    {
        if (supplied is null || supplied.Incarnation != identity.Incarnation
            || supplied.ReadGeneration != identity.ReadGeneration || !IsValid(supplied))
        { return new(identity.Incarnation, identity.ReadGeneration, null, null); }
        return new(identity.Incarnation, identity.ReadGeneration, supplied.UpperKey?.ToArray(), supplied.LastKey?.ToArray());
    }

    private static bool IsValid(RemoteTransferPendingCursor supplied)
        => supplied.UpperKey is null ? supplied.LastKey is null
            : IsKey(supplied.UpperKey) && (supplied.LastKey is null
                || IsKey(supplied.LastKey) && supplied.LastKey.AsSpan().SequenceCompareTo(supplied.UpperKey) <= RemoteTransferCoordinationProtocol.FirstIndex);

    private static bool IsKey(byte[] key)
        => key.Length <= DueWorkProtocol.MaximumKeyBytes && key.AsSpan().StartsWith(Prefix);
}
