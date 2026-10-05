using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsAuthorityFaultProvisioning
{
    private const string PrincipalPrefix = "c1-probe-";
    private const string CredentialSuffix = "-key";
    private const int SecretBytes = 32;

    internal static string NewPrivateRootPath()
        => Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification", "c1-held-authority-" + Guid.NewGuid().ToString("N"));

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

    internal static Dictionary<string, string> CurrentImages(string image)
        => new(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = image,
            [RequestCqrsRf3Protocol.Node2] = image,
            [RequestCqrsRf3Protocol.Node3] = image
        };

    internal static async Task<RequestCqrsAuthorityFaultIdentity> CreateIdentityAsync(
        KeyLoadClient administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        var suffix = Guid.NewGuid().ToString("N");
        var id = PrincipalPrefix + suffix;
        var partition = new PartitionRef(id, RequestCqrsRf3Protocol.Database, id, suffix);
        var principal = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection,
                Capability.DocumentsRead | Capability.DocumentsWrite)], []);
        var keyId = id + CredentialSuffix;
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var credential = new ApiKeyRecord(keyId, id,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        await ConfigureResourceAsync(administrator, partition, cancellationToken).ConfigureAwait(false);
        await ConfigurePrincipalAsync(administrator, principal, cancellationToken).ConfigureAwait(false);
        await ConfigureCredentialAsync(administrator, credential, cancellationToken).ConfigureAwait(false);
        await SeedDocumentAsync(administrator, partition, cancellationToken).ConfigureAwait(false);
        return new(partition, principal, credential, secret);
    }

    internal static PrincipalRecord Revoke(PrincipalRecord principal)
        => principal with { Revoked = true, PolicyEpoch = checked(principal.PolicyEpoch + 1) };

    internal static CommandRequest Replacement(RequestCqrsAuthorityFaultIdentity identity, Guid commandId,
        string json, long expectedRevision)
        => new(commandId, identity.Partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId,
                json, expectedRevision, ExplicitReplacement: true)]);

    private static async Task ConfigureResourceAsync(KeyLoadClient administrator, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var definition = new ResourceDefinition(RequestCqrsRf3Protocol.AdminCollection, ResourceKind.Collection,
            partition.TransactionDomainId);
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(), request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static async Task ConfigurePrincipalAsync(KeyLoadClient administrator, PrincipalRecord principal,
        CancellationToken cancellationToken)
    {
        var stored = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(stored.Id).IsEqualTo(principal.Id);
        await Assert.That(stored.ClusterAdministrator).IsFalse();
    }

    private static async Task ConfigureCredentialAsync(KeyLoadClient administrator, ApiKeyRecord credential,
        CancellationToken cancellationToken)
    {
        var saved = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(),
            credential, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(saved).IsTrue();
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
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(1L);
    }
}
