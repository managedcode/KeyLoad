using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed record AnalyticsStoreTestCommit(Command<DatabaseCommand> Operation, CommitReceipt Receipt);
