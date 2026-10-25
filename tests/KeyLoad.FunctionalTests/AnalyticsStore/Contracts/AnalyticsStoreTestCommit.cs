using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal sealed record AnalyticsStoreTestCommit(Command<DatabaseCommand> Operation, CommitReceipt Receipt);
