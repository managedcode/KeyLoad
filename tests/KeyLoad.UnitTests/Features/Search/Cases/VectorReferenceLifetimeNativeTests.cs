using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class VectorReferenceLifetimeNativeTests
{
    [Test]
    public async Task GenuineDropRecreateRefusesStoredAndSubmittedOldVectorSourceThenFreshLineageSurvivesTwoColdOwners()
    {
        var failures = new List<Exception>();
        var token = TestContext.Current!.Execution.CancellationToken;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var profile = await NativeDropRuntimeProfile.CreateAsync(token);
            await ServerFailureObserver.ObserveAsync(() => VectorReferenceLifetimeNativeFlow.RunAsync(profile, token), failures);
            if (failures.Count != 0)
            {
                profile.RetainRoots();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
