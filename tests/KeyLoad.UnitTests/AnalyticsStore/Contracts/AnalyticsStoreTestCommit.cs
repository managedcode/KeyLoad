using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.AnalyticsStore;

internal sealed record AnalyticsStoreTestCommit(Command<DatabaseCommand> Operation, CommitReceipt Receipt);
