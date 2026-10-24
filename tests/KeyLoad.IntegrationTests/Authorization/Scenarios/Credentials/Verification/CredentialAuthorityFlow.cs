using System.Net;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.DocumentStorage;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class CredentialAuthorityFlow
{
    private const string MissingRepairResult = "Original acknowledged credential repair is absent.";

    internal static async Task RunAsync(ClusterFixture fixture, CredentialAuthorityState state, bool expire, CancellationToken token)
    {
        await CredentialAuthorityClients.RunAsync(fixture, state, expire, changeCredential: true, token);
        await CredentialAuthorityCold.RunAsync(fixture, state, expire, token);
    }

    internal static async Task WarmAsync(ClusterFixture fixture, KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, CredentialAuthorityState state, bool expire, CancellationToken token)
    {
        var identity = state.Document.Identity;
        var unusable = expire ? identity.Credential with
        {
            ExpiresAt = TimeProvider.System.GetUtcNow() - McpCallerProtocol.ExpiredOffset
        }
            : identity.Credential with
            {
                Revoked = true
            };
        var changed = await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(), unusable, token));
        await Assert.That(changed.IsSuccess).IsTrue();
        await RefusedAsync(fixture, root, reader, session, state, token);
        await Assert.That((await PublicReadAssertions.DocumentAsync(await root.GetAsync(state.Document.Scenario.Reference, token)))!.Revision)
            .IsEqualTo(McpCallerProtocol.FirstRevision);
        var repaired = await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(
            state.RepairCommandId, identity.Credential, token));
        state.OriginalRepairResult = repaired;
        await Assert.That(repaired.IsSuccess).IsTrue();
        await Assert.That(repaired.Problem).IsNull();
        await VerifyStatusRoutesAsync(fixture, root, state, token);
        await DiscoveryAuthorityValues.HealthyAsync(root, reader, session, state.Document, token);
        await ReplayAsync(root, state, token);
    }

    internal static async Task VerifyStatusRoutesAsync(ClusterFixture fixture, KeyLoadClient root,
        CredentialAuthorityState state, CancellationToken token)
    {
        var before = await DiscoveryAuthorityValues.ImageAsync(root, state.Document, token);
        var request = new ConfigureApiKeyRequest(state.Document.Identity.Credential);
        var call = SqlClusterProtocol.Call(state.Document.Scenario.Partition, McpCallerTools.CredentialsConfigure,
            request, state.RepairCommandId);
        var sql = await McpCallerAssertions.SdkSuccessAsync(await root.ExecuteSqlAsync(call, token));
        await Assert.That(sql.ValueKind).IsEqualTo(JsonValueKind.Null);
        var failures = new List<Exception>();
        try
        {
            await using var administrator = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, token);
            try
            {
                var arguments = McpOfficialClient.Arguments(request);
                arguments.Add(McpCallerProtocol.CommandId, state.RepairCommandId);
                var direct = await McpCallerAssertions.StatusAsync(await administrator.Client.InvokeKeyLoadToolAsync(
                    McpCallerTools.CredentialsConfigure, arguments, token));
                var q1 = await McpCallerAssertions.StatusAsync(await administrator.CallAsync(SqlOperationProtocol.ToolName, call, token));
                await Assert.That(q1).IsNotEqualTo(direct);
            }
            catch (Exception original) when (CqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
            catch (Exception original) when (!CqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
        }
        catch (Exception acquisitionOrCleanup) when (CqrsBoundaryErrors.IsNonFatal(acquisitionOrCleanup)) { failures.Add(acquisitionOrCleanup); }
        catch (Exception acquisitionOrCleanup) when (!CqrsBoundaryErrors.IsNonFatal(acquisitionOrCleanup)) { failures.Add(acquisitionOrCleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        await SqlClusterProtocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state.Document, token));
    }

    private static async Task RefusedAsync(ClusterFixture fixture, KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, CredentialAuthorityState state, CancellationToken token)
    {
        var document = state.Document;
        var before = await DiscoveryAuthorityValues.ImageAsync(root, document, token);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(document.Scenario.Reference), token));
        await Assert.That(error).IsNotNull();
        await Assert.That(error?.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error?.Message.Contains(document.Identity.Secret, StringComparison.Ordinal) == true).IsFalse();
        await Assert.That(error?.Message.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal) == true).IsFalse();
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node2, document.Identity.Secret, token);
        var rejected = await reader.GetAsync(document.Scenario.Reference, token);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Value).IsNull();
        await Assert.That(rejected.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var serialized = JsonSerializer.Serialize(rejected, JsonDefaults.Options);
        await Assert.That(serialized.Contains(document.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(serialized.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        await SqlClusterProtocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, document, token));
    }

    internal static async Task ReplayAsync(KeyLoadClient root, CredentialAuthorityState state, CancellationToken token)
    {
        if (state.OriginalRepairResult is not { IsSuccess: true, Problem: null } original)
        {
            throw new InvalidOperationException(MissingRepairResult);
        }
        var before = await DiscoveryAuthorityValues.ImageAsync(root, state.Document, token);
        var replay = await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(
            state.RepairCommandId, state.Document.Identity.Credential, token));
        await Assert.That(replay.IsSuccess).IsEqualTo(original.IsSuccess);
        await Assert.That(replay.Problem).IsEqualTo(original.Problem);
        await SqlClusterProtocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state.Document, token));
    }
}
