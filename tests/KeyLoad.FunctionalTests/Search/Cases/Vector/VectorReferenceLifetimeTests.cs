using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class VectorReferenceLifetimeTests
{
    [Test]
    public async Task GenuineDropRecreateRefusesStoredAndSubmittedOldVectorSourceThenFreshLineageSurvivesTwoColdOwners()
    {
        var failures = new List<Exception>();
        var token = TestContext.Current!.Execution.CancellationToken;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var profile = await DropRuntimeProfile.CreateAsync(token);
            await ServerFailureObserver.ObserveAsync(() => VectorReferenceLifetimeFlow.RunAsync(profile, token), failures);
            if (failures.Count != 0)
            {
                profile.RetainRoots();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
