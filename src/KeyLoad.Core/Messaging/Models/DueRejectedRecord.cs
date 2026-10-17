namespace KeyLoad.Core.Features.Messaging;

internal sealed record DueRejectedRecord(DueWorkKind Kind, ErrorCode Code);
