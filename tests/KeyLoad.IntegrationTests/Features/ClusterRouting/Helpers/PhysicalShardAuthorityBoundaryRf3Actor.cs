using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PhysicalShardAuthorityBoundaryRf3Actor : IAsyncDisposable
{
    internal const string PrincipalId = "c1-probe-whole-plan-movement-operator";
    private const string Tenant = "protected-parent-tenant";
    private const string KeyPrefix = "whole-plan-movement-key-";
    private const string Separator = ".";
    private const string MissingCaller = "The owned movement caller is unavailable.";
    private const int SecretBytes = 32;
    private const long InitialEpoch = 1;
    private readonly string credential;
    private HttpClient? http;
    private McpOfficialClient? mcp;
    private PhysicalShardAuthorityBoundaryRf3Actor(string credential) => this.credential = credential;

    [AllowNull]
    internal KeyLoadClient Sdk
    {
        get => field ?? throw new InvalidOperationException(MissingCaller);
        private set;
    }
    internal McpOfficialClient Mcp => mcp ?? throw new InvalidOperationException(MissingCaller);

    internal static PrincipalRecord Definition(long epoch, bool administrator)
        => new(PrincipalId, Tenant, [], [])
        {
            ClusterAdministrator = administrator,
            PolicyEpoch = epoch
        };

    internal static async Task<PhysicalShardAuthorityBoundaryRf3Actor> CreateAsync(TwoRf3MembershipWave wave,
        KeyLoadClient sourceRoot, KeyLoadClient targetRoot, CancellationToken token)
    {
        await Assert.That(PrincipalId).IsNotEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
        var key = KeyPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var credential = key + Separator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var verifier = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
        var apiKey = new ApiKeyRecord(key, PrincipalId, verifier);
        var principal = Definition(InitialEpoch, true);
        await ConfigureAsync(sourceRoot, principal, apiKey, token);
        await ConfigureAsync(targetRoot, principal, apiKey, token);
        var actor = new PhysicalShardAuthorityBoundaryRf3Actor(credential);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            actor.http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
            actor.Sdk = new(actor.http, credential, IntegrationClientOptions.Execution());
            await actor.ReconnectAsync(wave, token);
        }, failures);
        if (failures.Count != PhysicalShardAuthorityBoundaryRf3Flow.NoFailures)
        {
            await ServerFailureObserver.ObserveAsync(() => actor.DisposeAsync().AsTask(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return actor;
    }

    private static async Task ConfigureAsync(KeyLoadClient root, PrincipalRecord principal,
        ApiKeyRecord credential, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await PhysicalShardAuthorityBoundaryRf3Flow.ObserveOriginalAsync(
                root.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token))));
        await McpCallerAssertions.SdkStatusAsync(await PhysicalShardAuthorityBoundaryRf3Flow.ObserveOriginalAsync(
            root.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token)));
    }

    internal async Task ReconnectAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        if (mcp is { } previous)
        {
            mcp = null;
            await previous.DisposeAsync();
        }
        mcp = await McpOfficialClient.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node1, credential, token);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (mcp is { } owned)
        {
            mcp = null;
            await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures);
        }
        if (http is { } ownedHttp)
        {
            http = null;
            Sdk = null;
            ServerFailureObserver.Observe(ownedHttp.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
