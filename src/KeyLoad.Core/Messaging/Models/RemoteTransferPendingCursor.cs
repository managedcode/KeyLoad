namespace KeyLoad.Core;

internal sealed record RemoteTransferPendingCursor(Guid Incarnation, long ReadGeneration, byte[]? UpperKey, byte[]? LastKey);
