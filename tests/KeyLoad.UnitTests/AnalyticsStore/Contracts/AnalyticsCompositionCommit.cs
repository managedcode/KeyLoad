using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed record AnalyticsCompositionCommit(Command<DatabaseCommand> Operation, CommitReceipt Receipt, long CreatorEpoch);
