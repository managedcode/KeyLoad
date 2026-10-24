using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal sealed record CompositionPrefixPrepareCommand(CommandRequest Request, Command<DatabaseCommand> Original,
    byte[] OriginalBytes, CommitReceipt? Receipt, ErrorCode? Error, string? SafeDetail, ReadExecutionBudget Work);
