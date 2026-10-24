using KeyLoad.Server;

namespace KeyLoad.UnitTests.EventStreams;

internal static class SharedDomainWholeFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        List<Exception> failures = [];
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var state = new SharedDomainState();
                SharedDomainOperations.Configure(fixture, state);
                await SharedDomainIdentityAssertions.RequireAsync(state);
                await SharedDomainProducerFlow.RunAsync(fixture.Database, state, healthy: false, token);
                await SharedDomainRefusals.ProducersAsync(fixture.Database, state, token);
                await SharedDomainProcessingFlow.RunAsync(fixture.Database, state, token);
                await SharedDomainBindingFlow.RunAsync(fixture.Database, state, token);
                await SharedDomainHistoryOperations.AllAsync(fixture.Database, fixture.Store, state, token);
                await SharedDomainColdFlow.RunAsync(fixture, state, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
