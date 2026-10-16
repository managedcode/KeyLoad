using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
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
        var options = P.Options(root);
        options.Validate();
        var registrations = new ServiceCollection().AddRuntimeOptions(new ConfigurationBuilder().Build());
        registrations.AddSingleton(CrashExecutionOptions.Node(options));
        var services = registrations.BuildServiceProvider();
        var logging = LoggerFactory.Create(_ => { });
        var runtime = services.GetRequiredService<ServerRuntimeOptions>();
        var ownership = new PartitionHostOwnership();
        PartitionHost? host = null;
        var failures = new List<Exception>();
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

        ServerFailureObserver.Observe(services.Dispose, failures);
        ServerFailureObserver.Observe(logging.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
