using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class ResourcePolicyUpdateTests
{
    private const string Classification = "private-record";
    private const string ReadGrant = "record.secret.read";
    private const string UseGrant = "record.secret.use";

    [Test]
    public async Task AcRpol001PolicyOnlyApplySurvivesNativeReopenAndReusesItsOriginalReceipt()
    {
        using var fixture = new ResourcePolicyUpdateFixture();
        var previous = fixture.Resource();
        var replacement = previous with
        {
            FieldPolicies = [new(ResourcePolicyUpdateFixture.SecretField, Classification, ReadGrant, UseGrant)],
            HeaderPolicies = [new("/trace", "internal-header", "header.read", "header.use")],
            SchemaVersion = previous.SchemaVersion + 1
        };
        var operation = fixture.ConfigureResource(replacement, previous.SchemaVersion);
        var resourceBytesBefore = fixture.ResourceBytes()!;
        var documentBytesBefore = fixture.DocumentBytes()!;

        var first = fixture.Apply(operation);
        var receiptBytes = fixture.OutcomeBytes(operation.Id)!;
        var storedAfterApply = fixture.Resource();
        var resourceBytesAfter = fixture.ResourceBytes()!;
        var documentBytesAfter = fixture.DocumentBytes()!;
        fixture.Reopen();
        var reopened = fixture.Resource();
        var retry = fixture.Apply(operation);

        await Assert.That(first.Error).IsNull();
        await Assert.That(first.Get<ResourceDefinition>().SchemaVersion).IsEqualTo(previous.SchemaVersion + 1);
        await Assert.That(JsonData.Fingerprint(storedAfterApply)).IsEqualTo(JsonData.Fingerprint(replacement));
        await Assert.That(JsonData.Fingerprint(reopened)).IsEqualTo(JsonData.Fingerprint(replacement));
        await Assert.That(resourceBytesAfter.SequenceEqual(resourceBytesBefore)).IsFalse();
        await Assert.That(documentBytesAfter.SequenceEqual(documentBytesBefore)).IsTrue();
        await Assert.That(fixture.DocumentBytes()!.SequenceEqual(documentBytesBefore)).IsTrue();
        await Assert.That(retry.Error).IsEqualTo(first.Error);
        await Assert.That(retry.Json).IsEqualTo(first.Json);
        await Assert.That(fixture.OutcomeBytes(operation.Id)!.SequenceEqual(receiptBytes)).IsTrue();
    }

    [Test]
    public async Task AcRpol002LegacyIdenticalDefinitionIsAllowedButChangedDefinitionRequiresCas()
    {
        using var fixture = new ResourcePolicyUpdateFixture();
        var previous = fixture.Resource();
        var identical = fixture.Apply(fixture.ConfigureResource(previous));
        var changed = fixture.Apply(fixture.ConfigureResource(previous with
        {
            FieldPolicies = [new(ResourcePolicyUpdateFixture.SecretField, Classification)]
        }));

        await Assert.That(identical.Error).IsNull();
        await Assert.That(ResourcePolicyUpdateFixture.Failure(changed).Code)
            .IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(JsonData.Fingerprint(fixture.Resource())).IsEqualTo(JsonData.Fingerprint(previous));
    }

}
