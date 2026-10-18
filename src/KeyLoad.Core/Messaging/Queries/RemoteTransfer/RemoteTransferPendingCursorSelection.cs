using System.Collections.Immutable;
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
        {
            return new(identity.Incarnation, identity.ReadGeneration, null, null);
        }
        return supplied;
    }

    private static bool IsValid(RemoteTransferPendingCursor supplied)
        => supplied.UpperKey is null ? supplied.LastKey is null
            : supplied.UpperKey is { } upper && IsKey(upper) && (supplied.LastKey is null
                || supplied.LastKey is { } last && IsKey(last)
                    && last.AsSpan().SequenceCompareTo(upper.AsSpan()) <= RemoteTransferCoordinationProtocol.FirstIndex);

    private static bool IsKey(ImmutableArray<byte> key)
        => !key.IsDefault && key.Length <= DueWorkProtocol.MaximumKeyBytes && key.AsSpan().StartsWith(Prefix);
}
