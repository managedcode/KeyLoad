using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal sealed record AnalyticsCompositionCommit(Command<DatabaseCommand> Operation, CommitReceipt Receipt, long CreatorEpoch);
