using KeyLoad.Server;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.RelationalStorage;

internal sealed record SqlTypedInsertExecution(McpDecodedOperation Command, DateTimeOffset At,
    Result<object> Result, byte[] Bytes, CommitToken ExpectedToken);
