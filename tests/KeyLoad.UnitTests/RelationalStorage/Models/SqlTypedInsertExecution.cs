using ManagedCode.Communication;
using KeyLoad.Server;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed record SqlTypedInsertExecution(McpDecodedOperation Command, DateTimeOffset At,
    Result<object> Result, byte[] Bytes, CommitToken ExpectedToken);
