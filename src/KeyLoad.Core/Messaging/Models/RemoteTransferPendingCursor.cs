using System.Collections.Immutable;

namespace KeyLoad.Core;

/// <summary>Owns bounded pending-transfer discovery keys from one actual store generation.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(RemoteTransferPendingCursor.SerializerAlias)]
public sealed record RemoteTransferPendingCursor([property: Orleans.Id(0)] Guid Incarnation,
    [property: Orleans.Id(1)] long ReadGeneration,
    [property: Orleans.Id(2)] ImmutableArray<byte>? UpperKey,
    [property: Orleans.Id(3)] ImmutableArray<byte>? LastKey)
{
    internal const string SerializerAlias = "keyload.core.queue-transfer-pending-cursor";
}
