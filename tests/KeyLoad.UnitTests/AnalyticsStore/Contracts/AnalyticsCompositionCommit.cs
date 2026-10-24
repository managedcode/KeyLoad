using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.AnalyticsStore;

internal sealed record AnalyticsCompositionCommit(Command<DatabaseCommand> Operation, CommitReceipt Receipt, long CreatorEpoch);
