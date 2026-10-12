using System.Security.Cryptography;
using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferNativeAdministrator : IAsyncDisposable
{
    private const string CredentialId = "wire-parent-admin-key";
    private RemoteTransferNativeCallers? source;
    private RemoteTransferNativeCallers? target;
    private string? credential;
    internal KeyLoadClient Source => (source ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing)).Sdk;
    internal KeyLoadClient Target => (target ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing)).Sdk;

    internal async Task InitializeAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        credential = CredentialId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var principal = PartitionMovementPublicParentRf3Administrator.InitialDefinition();
        var record = DatabaseEngine.Credential(CredentialId, principal.Id, credential);
        foreach (var node in new[] { TwoRf3MembershipProtocol.Nodes[0], TwoRf3MembershipProtocol.Nodes[3] })
        {
            await using var admin = await RequestCqrsRf3Callers.ConnectAsync(wave.Application, node,
                wave.Profile.AdminKey, token);
            await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
            await Assert.That((await McpCallerAssertions.SdkStatusAsync(await admin.Sdk.ConfigureApiKeyAsync(
                Guid.NewGuid(), record, token))).IsSuccess).IsTrue();
        }
        await ReopenAsync(wave, token);
    }

    internal async Task ReopenAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        if (source is not null || target is not null || credential is null)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        source = await RemoteTransferNativeCallers.ConnectAsync(wave, TwoRf3MembershipProtocol.Nodes[0], credential, token);
        target = await RemoteTransferNativeCallers.ConnectAsync(wave, TwoRf3MembershipProtocol.Nodes[3], credential, token);
    }

    internal Task RequireAsync<TRequest, TResult>(PartitionRef partition, string tool, TRequest request,
        Func<CancellationToken, Task<Result<TResult>>> invoke, TResult expected, CancellationToken token)
        => (source ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing))
            .RequireAsync(partition, tool, request, invoke, expected, token);

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        var destination = target;
        var origin = source;
        target = null;
        source = null;
        if (destination is not null)
        { await ServerFailureObserver.ObserveAsync(() => destination.DisposeAsync().AsTask(), failures); }
        if (origin is not null)
        { await ServerFailureObserver.ObserveAsync(() => origin.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
