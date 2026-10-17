using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class RetirementConservationHeaderTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public Task InvalidCurrentImageProofRefusesBeforeNativeReplaceThenSameOwnerCommitsAndTwoColdReopens(int defect)
        => RunAsync((files, ownership) => RetirementConservationHeaderFlow.RunAsync(files, ownership, defect));

    [Test]
    public Task RecoveryReadDebtRefusesBeforeNativeReplaceThenOriginalOwnerCommitsAndTwoColdReopens()
        => RunAsync(RetirementConservationBudgetFlow.RunAsync);

    private static async Task RunAsync(Func<CurrentStorageFixture, StorageFailureOwnerLifetime, Task> body)
    {
        CurrentStorageFixture? files = null;
        var ownership = new StorageFailureOwnerLifetime();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            files = new();
            await body(files, ownership);
        }, failures);
        if (files is not null && ownership.CanAdvance && failures.Count == 0)
        {
            ServerFailureObserver.Observe(files.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
