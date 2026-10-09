using KeyLoad.Orleans;
using System.Security.Cryptography;
using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using ManagedCode.Communication;
using ModelContextProtocol.Client;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PartitionMovementBlobWireCallers : IAsyncDisposable
{
    private const int FirstOwner = 0;
    private const int SecretBytes = 32;
    private const string CredentialId = "wire-parent-admin-key";
    private readonly PartitionMovementLateNativeHttp source;
    private readonly PartitionMovementLateNativeHttp target;
    private HttpClientTransport? transport;
    private McpClient? official;
    internal KeyLoadClient Source { get; private set; } = null!;
    internal KeyLoadClient Target { get; private set; } = null!;

    internal PartitionMovementBlobWireCallers(PartitionMovementLateNativeSettings settings)
    {
        source = PartitionMovementLateNativeHttp.Create(new(settings.Origin(FirstOwner)));
        try { target = PartitionMovementLateNativeHttp.Create(new(settings.Origin(PartitionMovementLateNativeSettings.GroupSize))); }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try { source.Dispose(); } catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures); throw;
        }
    }

    internal async Task InitializeAsync(PartitionMovementLateNativeSettings settings, CancellationToken token)
    {
        var credential = CredentialId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var principal = PartitionMovementPublicParentRf3Administrator.InitialDefinition();
        var record = DatabaseEngine.Credential(CredentialId, principal.Id, credential);
        foreach (var client in new[] { new KeyLoadClient(source.Client, settings.AdministratorKey, IntegrationClientOptions.Execution()),
            new KeyLoadClient(target.Client, settings.AdministratorKey, IntegrationClientOptions.Execution()) })
        {
            await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                await client.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
            await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await client.ConfigureApiKeyAsync(Guid.NewGuid(), record, token))).IsTrue();
        }
        Source = new(source.Client, credential, IntegrationClientOptions.Execution());
        Target = new(target.Client, credential, IntegrationClientOptions.Execution());
        transport = new(new HttpClientTransportOptions
        {
            Endpoint = new Uri(source.Client.BaseAddress!, McpCallerProtocol.Endpoint),
            TransportMode = HttpTransportMode.StreamableHttp, EnableStandaloneGetStream = false,
            AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
            { [McpCallerProtocol.AuthorizationHeader] = McpCallerProtocol.BearerPrefix + credential }
        }, source.Client);
        official = await McpClient.CreateAsync(transport, new McpClientOptions
            { ProtocolVersion = McpCallerProtocol.ProtocolVersion }, cancellationToken: token);
    }

    internal async Task RequireAsync<TRequest, TResult>(PartitionRef partition, string tool, TRequest request,
        Func<CancellationToken, Task<Result<TResult>>> invoke, TResult expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await invoke(token)));
        var client = official ?? throw new InvalidOperationException("The actual official MCP client is absent.");
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<TResult>(
            await client.InvokeKeyLoadToolAsync(tool, McpOfficialClient.Arguments(request), token))).Value);
        var sql = SqlRf3Protocol.Call(partition, tool, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<TResult>(Source, sql, token));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<TResult>(
            await client.InvokeKeyLoadToolAsync(SqlOperationProtocol.ToolName, McpOfficialClient.Arguments(sql), token))).Value);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (official is { } client) { await ServerFailureObserver.ObserveAsync(() => client.DisposeAsync().AsTask(), failures); }
        if (transport is not null)
        {
            try { await transport.DisposeAsync(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        try { target.Dispose(); } catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try { source.Dispose(); } catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
