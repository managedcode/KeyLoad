using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using C = KeyLoad.IntegrationTests.Features.DocumentStorage.RemoteDocumentRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class DocumentLifetimeRf3Flow
{
    private const int ColdCount = 2;
    private const long Third = 3;
    private const long Fourth = 4;
    private const long Fifth = 5;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token);
        var seed = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey,
            source => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey,
                target => RemoteDocumentRf3Seed.CreateAsync(wave, source.Sdk, target.Sdk, probe: true, token), token), token);
        await RoundAsync(wave, seed, initial: true, token);
        for (var cold = default(int); cold < ColdCount; cold++)
        {
            _ = await wave.StopForDirectoryReadAsync();
            await wave.RestartJoinedAsync(token);
            await RoundAsync(wave, seed, initial: false, token);
        }
    }

    private static async Task RoundAsync(TwoRf3MembershipWave wave, RemoteDocumentRf3Seed seed, bool initial, CancellationToken token)
        => _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey,
            source => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey,
                target => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node2, seed.Secret, async reader =>
                {
                    if (initial)
                    {
                        await RemoteDocumentRf3Assertions.DeniedAsync(reader.Sdk, token);
                        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(C.DocumentsGet,
                            new GetDocumentRequest(C.Reference), token), ErrorCode.PermissionDenied, dispatched: true);
                        await seed.GrantDestinationAsync(target.Sdk, token);
                        await DocumentLifetimeRf3Assertions.HealthyAsync(reader, source.Sdk, target.Sdk, seed, token);
                        await DocumentLifetimeRf3Assertions.PrincipalAsync(source.Sdk, seed, C.GrantedPolicyEpoch, raw: false, token);
                        await DocumentLifetimeRf3Assertions.PrincipalAsync(source.Sdk, seed, Third, raw: false, token);
                        await DocumentLifetimeRf3Assertions.PrincipalAsync(target.Sdk, seed, Third, raw: true, token);
                        await DocumentLifetimeRf3Assertions.HealthyAsync(reader, source.Sdk, target.Sdk, seed, token);
                        await DocumentLifetimeRf3Assertions.PrincipalAsync(source.Sdk, seed, Fourth, raw: true, token);
                        await DocumentLifetimeRf3Assertions.PrincipalAsync(target.Sdk, seed, Fourth, raw: false, token);
                        await DocumentLifetimeRf3Assertions.HealthyAsync(reader, source.Sdk, target.Sdk, seed, token);
                        await HeldAsync(wave, source, reader, seed, cancel: false, token);
                        await HeldAsync(wave, source, reader, seed, cancel: true, token);
                    }
                    await DocumentLifetimeRf3Assertions.HealthyAsync(reader, source.Sdk, target.Sdk, seed, token);
                    await DocumentLifetimeRf3Assertions.ReplayAsync(target, seed, token);
                    return true;
                }, token), token), token);

    private static async Task HeldAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers source,
        RequestCqrsRf3Callers reader, RemoteDocumentRf3Seed seed, bool cancel, CancellationToken token)
    {
        var discovery = await RequestCqrsAuthorityFaultDiscovery.ReadAsync(wave.Application, wave.Profile, token);
        var hold = new DocumentLifetimeRf3Hold(wave.QueryControls, seed.Principal.Id);
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = reader.Sdk.GetAsync(C.Reference, seed.Receipt.Token, selected.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await hold.WaitAsync(original, discovery, token);
            if (cancel)
            {
                await selected.CancelAsync();
            }
            else
            {
                await DocumentLifetimeRf3Assertions.PrincipalAsync(source.Sdk, seed, Fifth, raw: true, token);
            }
        }, failures);
        ServerFailureObserver.Observe(hold.Release, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = await original;
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Value).IsNull();
            var problem = result.Problem ?? throw new InvalidOperationException(C.DocumentsGet);
            await Assert.That(problem.ErrorCode).IsEqualTo((cancel ? ErrorCode.Cancelled : ErrorCode.OwnershipLost).ToString());
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => hold.JoinAsync(discovery, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
