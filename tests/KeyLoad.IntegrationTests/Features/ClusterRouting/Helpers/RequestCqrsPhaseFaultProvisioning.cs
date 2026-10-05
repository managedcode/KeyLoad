using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Creates one isolated, persisted non-administrator document identity through public admin operations.</summary>
internal static class RequestCqrsPhaseFaultProvisioning
{
    private const string PrincipalPrefix = "c1-probe-";
    private const string KeySuffix = "-key";
    private const int SecretBytes = 32;

    internal static async Task<RequestCqrsPhaseFaultIdentity> CreatePersistedIdentityAsync(
        KeyLoadClient administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        var suffix = Guid.NewGuid().ToString("N");
        var partition = new PartitionRef(PrincipalPrefix + suffix, RequestCqrsRf3Protocol.Database,
            PrincipalPrefix + suffix, suffix);
        var principalId = PrincipalPrefix + suffix;
        var credentialId = principalId + KeySuffix;
        var secret = credentialId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        await ConfigureIdentityAsync(administrator, partition, principalId, credentialId, secret, cancellationToken)
            .ConfigureAwait(false);
        await SeedDocumentAsync(administrator, partition, cancellationToken).ConfigureAwait(false);
        return new(partition, principalId, secret);
    }

    internal static Task<RequestCqrsRf3Callers> ConnectAsync(DistributedApplication app,
        RequestCqrsPhaseFaultIdentity identity, CancellationToken cancellationToken)
        => RequestCqrsRf3Callers.ConnectAsync(app, RequestCqrsRf3Protocol.Node1,
            identity.Secret, cancellationToken);

    internal static CommandRequest UpdateCommand(RequestCqrsPhaseFaultIdentity identity, Guid commandId)
        => new(commandId, identity.Partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId,
                RequestCqrsRf3Protocol.ChangedDocumentJson, ExpectedRevision: 1, ExplicitReplacement: true)]);

    private static async Task ConfigureIdentityAsync(KeyLoadClient administrator, PartitionRef partition,
        string principalId, string credentialId, string secret, CancellationToken cancellationToken)
    {
        var principal = new PrincipalRecord(principalId, partition.TenantId,
            [new(partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection,
                Capability.DocumentsRead | Capability.DocumentsWrite)], []) { ClusterAdministrator = false };
        var credential = new ApiKeyRecord(credentialId, principalId,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        var definition = new ResourceDefinition(RequestCqrsRf3Protocol.AdminCollection, ResourceKind.Collection,
            partition.TransactionDomainId);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition), cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var stored = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(stored.Id).IsEqualTo(principalId);
        await Assert.That(stored.ClusterAdministrator).IsFalse();
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(
            Guid.NewGuid(), credential, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false)).IsTrue();
    }

    private static async Task SeedDocumentAsync(KeyLoadClient administrator, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId,
                RequestCqrsRf3Protocol.DocumentJson, ExpectedRevision: 0)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(receipt.Mutations[0].Resource).IsEqualTo(RequestCqrsRf3Protocol.AdminCollection);
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(RequestCqrsRf3Protocol.DocumentId);
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(1L);
    }

    internal static string NewPrivateRootPath()
        => Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification", "c1-public-interruption-" + Guid.NewGuid().ToString("N"));

    internal static void CreatePrivateRoot(string root, Action markOwned)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(markOwned);
        if (Directory.Exists(root) || File.Exists(root))
        { throw new IOException("The isolated C1 test root already exists."); }
        Directory.CreateDirectory(root);
        markOwned();
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    internal static Dictionary<string, string> CurrentImages(string currentImage)
        => new(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = currentImage,
            [RequestCqrsRf3Protocol.Node2] = currentImage,
            [RequestCqrsRf3Protocol.Node3] = currentImage
        };
}
