using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Replication;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using P = KeyLoad.CrashHost.Features.AnalyticsStore.UserAnalyticsProcessProtocol;

namespace KeyLoad.CrashHost.Features.AnalyticsStore;

internal static class UserAnalyticsProcessHost
{
    internal static async Task RunAsync(string root, Func<PartitionHost, ServerRuntimeOptions, Task> body)
    {
        var registrations = new ServiceCollection().AddRuntimeOptions(new ConfigurationBuilder().Build());
        registrations.AddSingleton(UserAnalyticsProcessOptions.Create(root));
        var failures = new List<Exception>();
        try
        {
            using var logging = LoggerFactory.Create(_ => { });
            try
            {
                using var services = registrations.BuildServiceProvider();
                await ServerFailureObserver.ObserveAsync(() => RunHostAsync(services, logging, body, failures), failures);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
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

    private static async Task RunHostAsync(IServiceProvider services, ILoggerFactory logging,
        Func<PartitionHost, ServerRuntimeOptions, Task> body, List<Exception> failures)
    {
        var runtime = services.GetRequiredService<ServerRuntimeOptions>();
        var ownership = new PartitionHostOwnership();
        PartitionHost? host = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            host = await PartitionHost.OpenAsync(runtime, new AuthorizationPolicy(),
                new CommandAdmissionGovernor(runtime.CommandAdmission), TimeProvider.System,
                logging.CreateLogger<ReplicaConsensus>(), ownership);
            await body(host, runtime);
        }, failures);
        if (host is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => host.DisposeAsync().AsTask(), failures);
        }
        if (!ownership.CanReleaseRoot)
        {
            failures.Add(new IOException(P.Invalid));
        }
    }
}
