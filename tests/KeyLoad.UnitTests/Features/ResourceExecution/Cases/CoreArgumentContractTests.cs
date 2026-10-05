using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CoreArgumentContractTests
{
    private const string RootPrincipal = "root";
    private const string CredentialId = "guard-key";
    private const string CredentialSecret = "guard-key.real-credential-buffer-32-characters";
    private const string CollectionName = "guard-docs";
    private const string KeySpaceName = "guard-space";

    [Test]
    public async Task AcCq001CoreNullArgumentsRejectBeforeProviderWork()
    {
        using var database = new TestDatabase();
        var credential = DatabaseEngine.Credential(CredentialId, RootPrincipal, CredentialSecret);
        var before = database.Store.GetReadDiagnostics();

        Assert.ThrowsExactly<ArgumentNullException>(() => database.Database.Bootstrap(null!, credential));
        Assert.ThrowsExactly<ArgumentNullException>(() => database.Database.Authenticate(null!, TimeProvider.System.GetUtcNow()));
        Assert.ThrowsExactly<ArgumentNullException>(() => database.Database.Apply(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => database.Database.ResolveOutcome(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => database.Database.Resource(null!, database.Partition, CollectionName));
        Assert.ThrowsExactly<ArgumentNullException>(() => DatabaseEngine.Token(null!, database.Partition, 0));
        Assert.ThrowsExactly<ArgumentNullException>(() => DatabaseEngine.ValidatePartition(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => KeySpace.Partition(KeySpaceName, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => DatabaseEngine.Credential(CredentialId, RootPrincipal, null!));

        await Assert.That(database.Store.GetReadDiagnostics()).IsEqualTo(before);
    }
}
