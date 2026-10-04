using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using TUnit.Core.Interfaces;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RequestCqrsCohortDataSourceAttribute
    : DataSourceGeneratorAttribute<RequestCqrsCohortRuntimeFixture>
{
    protected override IEnumerable<Func<RequestCqrsCohortRuntimeFixture>> GenerateDataSources(
        DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<RequestCqrsCohortRuntimeFixture>(
            SharedType.PerTestSession,
            metadata,
            null,
            static () => new RequestCqrsCohortRuntimeFixture());
    }
}

internal sealed class RequestCqrsCohortRuntimeFixture : IAsyncInitializer, IAsyncDisposable
{
    internal static readonly TimeSpan StartupBound = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan ShutdownBound = TimeSpan.FromSeconds(30);

    private readonly TestCluster cluster;
    private bool disposed;

    internal RequestCqrsCohortRuntimeFixture()
    {
        cluster = new TestClusterBuilder(3).Build();
    }

    internal ILocalSiloDetails LocalSilo { get; private set; } = null!;
    internal IReadOnlyList<string> RuntimeAddresses { get; private set; } = [];

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(StartupBound);
        try
        {
            await cluster.DeployAsync(deadline.Token);
            var silos = cluster.Silos.OfType<InProcessSiloHandle>().ToArray();
            if (silos.Length < 3)
            {
                throw new InvalidOperationException("The real Orleans test cluster did not start three silos.");
            }

            var services = silos[0].ServiceProvider;
            LocalSilo = services.GetRequiredService<ILocalSiloDetails>();
            RuntimeAddresses = silos.Select(silo => silo.SiloAddress.ToParsableString()).ToArray();
        }
        catch (Exception startupFailure)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(startupFailure, cleanupFailure);
            }

            throw;
        }
    }

    internal string RuntimeAddress(int index) => RuntimeAddresses[index];

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        using var deadline = new CancellationTokenSource(ShutdownBound);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => cluster.StopAllSilosAsync(deadline.Token), failures);
        try
        {
            await cluster.DisposeAsync();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
