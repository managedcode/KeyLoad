using System.Security.Cryptography;
using System.Text;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class RuntimeJournalStartup
{
    private const int GuidBytes = 16;
    private const int BootstrapIdentityCount = 2;
    private const string BootstrapCommandDomain = "keyload-runtime-journal-bootstrap-v1\0";
    private static readonly byte[] BootstrapDomain = Encoding.ASCII.GetBytes(BootstrapCommandDomain);
    internal static async Task InitializeAsync(IServiceProvider services, PartitionHost partition,
        PrincipalRecord administrator, NodeOptions options, ServerRuntimeOptions runtime,
        CancellationToken cancellationToken)
    {
        var clock = services.GetRequiredService<TimeProvider>();
        using var deadline = new CancellationTokenSource(runtime.GrainRouting.Value.ExecutionLifetime, clock);
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = pending.Token;
        var requests = new RuntimeJournalStartupRequests(services.GetRequiredService<IGrainFactory>(),
            services.GetRequiredService<GrainRequestCodec>(), partition.Database, partition.Coordinator,
            services, clock, runtime.GrainRouting);
        if (!await requests.TryVerifyAsync(token).ConfigureAwait(false))
        {
            var discovery = services.GetRequiredService<ReplicaSiloDiscoveryClient>();
            while (!await discovery.HasNativeJournalCohortAsync(token).ConfigureAwait(false))
            {
                await Task.Delay(runtime.DurableJobs.Value.BootstrapPollInterval, clock, token).ConfigureAwait(false);
            }
            await requests.BootstrapAsync(administrator, BootstrapCommandId(options.PhysicalShardId,
                partition.Configuration.Incarnation), token).ConfigureAwait(false);
        }
        else
        {
            var discovery = services.GetRequiredService<ReplicaSiloDiscoveryClient>();
            await discovery.EnsureCompatibleCohortAsync(token).ConfigureAwait(false);
        }
        services.GetRequiredService<RuntimeJournalAdmission>().Open(token);
    }

    private static Guid BootstrapCommandId(Guid shardId, Guid incarnation)
    {
        Span<byte> bytes = stackalloc byte[BootstrapDomain.Length + BootstrapIdentityCount * GuidBytes];
        BootstrapDomain.CopyTo(bytes);
        shardId.TryWriteBytes(bytes[BootstrapDomain.Length..], bigEndian: true, out _);
        incarnation.TryWriteBytes(bytes[(BootstrapDomain.Length + GuidBytes)..], bigEndian: true, out _);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, digest);
        return new Guid(digest[..GuidBytes], bigEndian: true);
    }
}
