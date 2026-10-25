using KeyLoad.Core.BlobStorage;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.BlobStorage;

internal sealed class FilePhysicalIdentityTests
{
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Domain = "domain";
    private const string Partition = "partition";
    private const string Resource = "files";
    private const string OtherResource = "Files";
    private const string Path = "a.txt";
    private const string OtherPath = "b.txt";
    private const string Caller = "alice";
    private const string OtherCaller = "bob";
    private const string PublicSession = "upload-1";
    private const string Incarnation = "one";
    private const string OtherIncarnation = "two";

    [Test]
    public async Task PhysicalNamespacesPreserveScopeCaseAndIncarnationWhileSessionsIsolateCallers()
    {
        var address = new FileAddress(new(Tenant, Database, Domain, Partition), Resource, Path);
        var scope = FilePhysicalIdentity.Resource(address, new(Incarnation));
        await Assert.That(FilePhysicalIdentity.Resource(address with { Path = OtherPath }, new(Incarnation))).IsEqualTo(scope);
        await Assert.That(FilePhysicalIdentity.Resource(address with { Resource = OtherResource }, new(Incarnation))).IsNotEqualTo(scope);
        await Assert.That(FilePhysicalIdentity.Resource(address, new(OtherIncarnation))).IsNotEqualTo(scope);
        var session = FilePhysicalIdentity.Upload(scope, Caller, PublicSession);
        await Assert.That(FilePhysicalIdentity.Upload(scope, Caller, PublicSession)).IsEqualTo(session);
        await Assert.That(FilePhysicalIdentity.Upload(scope, OtherCaller, PublicSession)).IsNotEqualTo(session);
    }
}
