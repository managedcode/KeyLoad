using ManagedCode.Communication.Commands;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionPrefixPrepareCommand(CommandRequest Request, Command<DatabaseCommand> Original,
    byte[] OriginalBytes, CommitReceipt? Receipt, ErrorCode? Error, string? SafeDetail, ReadExecutionBudget Work);
