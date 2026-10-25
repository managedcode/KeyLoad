using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed record TransactionalReadViewObserved(string[] Rows, ZoneTreeReadCutVisitResult Result);
