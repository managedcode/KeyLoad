using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

internal sealed record RemoteTransferPendingPage(RemoteTransferCoordinationHint? Hint, RemoteTransferPendingCursor Cursor);
