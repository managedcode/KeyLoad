using System.Security.Cryptography;
using System.Text;
using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class RuntimeJournalStartup
{
    private const int GuidBytes = 16;
    private const int BootstrapIdentityCount = 2;
    private const string BootstrapCommandDomain = "keyload-runtime-journal-bootstrap\0";
    private static readonly byte[] BootstrapDomain = Encoding.ASCII.GetBytes(BootstrapCommandDomain);
    internal static async Task InitializeAsync(IServiceProvider services,
        Func<CancellationToken, Task<PrincipalRecord>> authenticateAdministrator, ServerRuntimeOptions runtime,
        CancellationToken cancellationToken)
    {
        var clock = services.GetRequiredService<TimeProvider>();
        using var deadline = new CancellationTokenSource(runtime.GrainRouting.Value.ExecutionLifetime, clock);
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = pending.Token;
        var requests = new RuntimeJournalStartupRequests(services.GetRequiredService<IGrainFactory>(),
            services.GetRequiredService<GrainRequestCodec>(), services, clock, runtime.GrainRouting);
        var incarnation = runtime.Node.Value.Incarnation;
        var commandId = BootstrapCommandId(incarnation, incarnation);
        await InitializeAuthorityAsync(requests, authenticateAdministrator, commandId, runtime, clock, token);
        services.GetRequiredService<RuntimeJournalAdmission>().Open(token);
    }

    private static async Task InitializeAuthorityAsync(RuntimeJournalStartupRequests requests,
        Func<CancellationToken, Task<PrincipalRecord>> authenticateAdministrator, Guid commandId,
        ServerRuntimeOptions runtime, TimeProvider clock, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (await requests.TryVerifyAsync(token))
                {
                    return;
                }
                var administrator = await authenticateAdministrator(token);
                await requests.BootstrapAsync(administrator, commandId, token);
                return;
            }
            catch (KeyLoadException error) when (error.Code is ErrorCode.UnknownWriteOutcome or ErrorCode.OwnershipLost)
            {
                await Task.Delay(runtime.DurableJobs.Value.BootstrapPollInterval, clock, token);
            }
        }
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
