using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

internal sealed record RemoteTransferPendingCursor(Guid Incarnation, long ReadGeneration, byte[]? UpperKey, byte[]? LastKey);

internal sealed record RemoteTransferPendingPage(RemoteTransferCoordinationHint? Hint, RemoteTransferPendingCursor Cursor);
