using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PlacementCapacityAdmissionAssertions
{
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;
    private const string Bound = "The Control-owner placement apply admission is exhausted.";

    internal static async Task OverflowAsync(PlacementIntentAdmissions admission)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var unexpected = await admission.EnterAsync(CancellationToken.None);
        }, failures);
        await Assert.That(failures.Count).IsEqualTo(SingleFailure);
        await Assert.That(failures[FirstFailure]).IsTypeOf<KeyLoadException>();
        var actual = (KeyLoadException)failures[FirstFailure];
        await RequireBoundAsync(actual);
    }

    internal static async Task RequireBoundAsync(KeyLoadException actual)
    {
        await Assert.That(actual.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(actual.Message).IsEqualTo(Bound);
        await Assert.That(actual.StatusCode).IsEqualTo(429);
        await Assert.That(actual.InnerException).IsNull();
        var problem = actual.ToProblem();
        await Assert.That(problem.Type).IsEqualTo("urn:keyload:error:ResourceExhausted");
        await Assert.That(problem.Title).IsEqualTo("ResourceExhausted");
        await Assert.That(problem.ErrorCode).IsEqualTo("ResourceExhausted");
        await Assert.That(problem.Detail).IsEqualTo(Bound);
        await Assert.That(problem.StatusCode).IsEqualTo(429);
    }

    internal static async Task ClosedAsync(PlacementIntentAdmissions admission)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var unexpected = await admission.EnterAsync(CancellationToken.None);
        }, failures);
        await Assert.That(failures.Count).IsEqualTo(SingleFailure);
        await Assert.That(failures[FirstFailure]).IsTypeOf<ObjectDisposedException>();
    }
}
