using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionPrefixPrepareCommand(CommandRequest Request, ReplicatedOperation Original,
    byte[] OriginalBytes, CommitReceipt? Receipt, ErrorCode? Error, string? SafeDetail, ReadExecutionBudget Work);
