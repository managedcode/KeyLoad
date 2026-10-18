using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionPrefixPrepareCommand(CommandRequest Request, Command<DatabaseCommand> Original,
    byte[] OriginalBytes, CommitReceipt? Receipt, ErrorCode? Error, string? SafeDetail, ReadExecutionBudget Work);
