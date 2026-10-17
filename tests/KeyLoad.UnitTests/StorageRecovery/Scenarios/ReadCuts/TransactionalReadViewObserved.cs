using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record TransactionalReadViewObserved(string[] Rows, ZoneTreeReadCutVisitResult Result);
