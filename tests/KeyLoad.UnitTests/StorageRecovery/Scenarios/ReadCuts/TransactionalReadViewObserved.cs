using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed record TransactionalReadViewObserved(string[] Rows, ZoneTreeReadCutVisitResult Result);
