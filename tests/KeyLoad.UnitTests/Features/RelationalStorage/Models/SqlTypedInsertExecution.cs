using KeyLoad.Server;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed record SqlTypedInsertExecution(McpDecodedOperation Command, DateTimeOffset At,
    OperationResult Result, byte[] Bytes, CommitToken ExpectedToken);
