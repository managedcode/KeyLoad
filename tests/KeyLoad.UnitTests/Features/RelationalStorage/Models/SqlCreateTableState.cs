using KeyLoad.Server;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed record SqlCreateTableState(McpDecodedOperation Created, DateTimeOffset CreatedAt,
    OperationResult Original, McpDecodedOperation Duplicate, DateTimeOffset DuplicateAt, OperationResult Failed, PrincipalRecord Principal, byte[] OriginalBytes, byte[] FailedBytes,
    bool EpochChanged = false, McpDecodedOperation? Repaired = null,
    DateTimeOffset RepairedAt = default, OperationResult? RepairedOutcome = null);
