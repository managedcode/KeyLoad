using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Genuine persisted invalid credentials never become trusted offline or restored public authority.</summary>
internal static class ClusterRestoreRf3CredentialTrial
{
    private const string KeyPrefix = "restore-invalid-";
    private const string Separator = ".";
    private const string ArchiveRoot = "credential-off-node";
    private const string RejectedRoot = "credential-refused-";
    private const string HealthyRoot = "credential-healthy";
    private const string Format = "N";
    private const int SecretBytes = 32;

    internal static async Task RunAsync(TwoRf3MembershipWave source, PartitionMovementPublicParentRf3Seed seed,
        string ownedRoot, CancellationToken cancellationToken)
    {
        await ClusterRestoreRf3CaptureAuthorityTrial.RunAsync(source, seed, cancellationToken).ConfigureAwait(false);
        var credentials = new List<string>();
        foreach (var revoked in new[] { true, false })
        {
            var id = KeyPrefix + Guid.NewGuid().ToString(Format);
            var secret = id + Separator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
            var row = new ApiKeyRecord(id, PartitionMovementPublicParentRf3Administrator.PrincipalId,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
                revoked ? null : TimeProvider.System.GetUtcNow(), revoked);
            foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node4 })
            {
                _ = await ClusterRestoreRf3CallerOwner.RunAsync(source.Application, node, source.Profile.AdminKey,
                    async (sdk, _) =>
                    {
                        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureApiKeyAsync(
                            Guid.NewGuid(), row, cancellationToken).ConfigureAwait(false))).IsTrue();
                        return true;
                    }, cancellationToken).ConfigureAwait(false);
            }
            credentials.Add(secret);
        }
        var originals = await ClusterRestoreRf3Capture.CaptureAsync(source, seed.Partition, Guid.NewGuid(),
            cancellationToken).ConfigureAwait(false);
        var archives = await ClusterRestoreRf3NativeArchive.CopyAsync(source, originals,
            Path.Combine(ownedRoot, ArchiveRoot), cancellationToken).ConfigureAwait(false);
        foreach (var credential in credentials)
        {
            await RequireRejectedCredentialAsync(source, ownedRoot, originals, archives, credential,
                cancellationToken).ConfigureAwait(false);
            await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives,
                cancellationToken).ConfigureAwait(false);
        }
        await source.RestartJoinedAsync(cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3Scenario.RunAsync(source, seed, Path.Combine(ownedRoot, HealthyRoot),
            credentials, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireRejectedCredentialAsync(TwoRf3MembershipWave source, string ownedRoot,
        System.Collections.Immutable.ImmutableArray<ClusterBackupOwnerReceipt> originals,
        System.Collections.Immutable.ImmutableArray<string> archives, string credential, CancellationToken cancellationToken)
    {
        var rejected = new ClusterRestoreRf3Fixture(Path.Combine(ownedRoot, RejectedRoot + Guid.NewGuid().ToString(Format)),
            source.Profile.AdminKey, originals, archives);
        Exception? initiatingFailure = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                await rejected.StartRejectedCredentialAsync(credential,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception original)
            {
                initiatingFailure = original;
                throw;
            }
            finally { await rejected.DisposeAsync().ConfigureAwait(false); cleanupCompleted = true; }
        }
        catch (Exception terminal)
        {
            if (initiatingFailure is not null && !cleanupCompleted)
            { throw new AggregateException(initiatingFailure, terminal); }
            throw;
        }
    }

    internal static async Task RequireTargetAsync(ClusterRestoreRf3Fixture target, PartitionRef partition,
        IReadOnlyList<string> credentials, CancellationToken cancellationToken)
    {
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node4 })
        {
            foreach (var credential in credentials)
            {
                using var http = McpCallerHttp.Create(target.Application, node);
                var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
                await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.GetAsync(new(partition,
                    KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3Tokens.Documents,
                    KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3Tokens.FirstId), cancellationToken)
                    .ConfigureAwait(false), ErrorCode.Unauthenticated);
                var sql = SqlRf3Protocol.Call(partition, McpCallerTools.DocumentsGet,
                    new GetDocumentRequest(new(partition,
                        KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3Tokens.Documents,
                        KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3Tokens.FirstId)));
                await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ExecuteSqlAsync(sql, cancellationToken)
                    .ConfigureAwait(false), ErrorCode.Unauthenticated);
            }
        }
    }
}
