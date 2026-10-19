using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

/// <summary>Reports one actual pending-transfer hint and its bounded continuation.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(RemoteTransferPendingPage.SerializerAlias)]
public sealed record RemoteTransferPendingPage([property: Orleans.Id(0)] RemoteTransferCoordinationHint? Hint,
    [property: Orleans.Id(1)] RemoteTransferPendingCursor Cursor)
{
    internal const string SerializerAlias = "keyload.core.queue-transfer-pending-page";
}
