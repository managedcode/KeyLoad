using KeyLoad.Server;
using ManagedCode.Communication;
namespace KeyLoad.FunctionalTests.RelationalStorage;

internal sealed record SqlTypedInsertExecution(McpDecodedOperation Command, DateTimeOffset At,
    Result<object> Result, byte[] Bytes, CommitToken ExpectedToken);
