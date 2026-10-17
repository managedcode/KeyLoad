namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class ResourcePolicyUpdateVisibilityTests
{
    private const string Classification = "private-record";
    private const string ReadGrant = "record.secret.read";
    private const string UseGrant = "record.secret.use";

    [Test]
    public async Task AcRpol003CurrentPersistedFieldPolicyControlsAReadImmediatelyAfterApply()
    {
        using var fixture = new ResourcePolicyUpdateFixture();
        var reference = new EntityRef(fixture.Partition, ResourcePolicyUpdateFixture.Collection,
            ResourcePolicyUpdateFixture.DocumentId);
        var before = fixture.Database.GetDocument(ResourcePolicyUpdateFixture.ReaderId, reference)!;
        var previous = fixture.Resource();
        var replacement = previous with
        {
            FieldPolicies = [new(ResourcePolicyUpdateFixture.SecretField, Classification, ReadGrant, UseGrant)],
            SchemaVersion = previous.SchemaVersion + 1
        };
        fixture.Apply(fixture.ConfigureResource(replacement, previous.SchemaVersion)).Get<ResourceDefinition>();

        var after = fixture.Database.GetDocument(ResourcePolicyUpdateFixture.ReaderId, reference)!;

        await Assert.That(before.Redacted).IsFalse();
        await Assert.That(before.Json).IsEqualTo(ResourcePolicyUpdateFixture.PublicDocument);
        await Assert.That(after.Redacted).IsTrue();
        await Assert.That(after.Json).IsEqualTo("{\"public\":\"visible\"}");
        await Assert.That(after.Revision).IsEqualTo(before.Revision);
    }
}
