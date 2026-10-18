using ManagedCode.Communication;
using KeyLoad.Server;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed record SqlCreateTableState(McpDecodedOperation Created, DateTimeOffset CreatedAt,
    Result<object> Original, McpDecodedOperation Duplicate, DateTimeOffset DuplicateAt, Result<object> Failed, PrincipalRecord Principal, byte[] OriginalBytes, byte[] FailedBytes,
    bool EpochChanged = false, McpDecodedOperation? Repaired = null,
    DateTimeOffset RepairedAt = default, Result<object>? RepairedOutcome = null);
