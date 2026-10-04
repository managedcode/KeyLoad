using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class ResourcePolicyUpdateRejectionTests
{
    private const string NewClassification = "private-record";
    private const string RelationalMachineKey = "id";

    [Test]
    public async Task AcRpol002RejectsMissingExpectedVersionAndStaleCasWithoutReplacingCurrentResource()
    {
        using var fixture = new ResourcePolicyUpdateFixture();
        var current = fixture.Resource();
        var missing = fixture.Apply(fixture.ConfigureResource(current with
        {
            FieldPolicies = [new(ResourcePolicyUpdateFixture.SecretField, NewClassification)]
        }));
        var stale = fixture.Apply(fixture.ConfigureResource(current with
        {
            FieldPolicies = [new(ResourcePolicyUpdateFixture.SecretField, NewClassification)],
            SchemaVersion = 2
        }, 0));

        await Assert.That(ResourcePolicyUpdateFixture.Failure(missing).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(ResourcePolicyUpdateFixture.Failure(stale).Code).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(JsonData.Fingerprint(fixture.Resource())).IsEqualTo(JsonData.Fingerprint(current));
    }

    [Test]
    public async Task AcRpol002RejectsMissingResourceCasBeforeAnyCatalogWrite()
    {
        using var fixture = new ResourcePolicyUpdateFixture();
        var operation = fixture.ConfigureResource(new("missing-policy-resource", ResourceKind.Collection,
            ResourcePolicyUpdateFixture.DomainId)
        { SchemaVersion = 2 }, 1);

        var failure = ResourcePolicyUpdateFixture.Failure(fixture.Apply(operation));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RevisionConflict);
        var missing = fixture.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            ResourcePolicyUpdateFixture.TenantId, ResourcePolicyUpdateFixture.DatabaseId, "missing-policy-resource")));
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async Task AcRpol002RejectsInvalidVersionNoopAndOverflowAsTypedFailures()
    {
        using var fixture = new ResourcePolicyUpdateFixture();
        var previous = fixture.Resource();
        var invalidIncrement = fixture.Apply(fixture.ConfigureResource(previous with
        {
            FieldPolicies = [new(ResourcePolicyUpdateFixture.SecretField, NewClassification)],
            SchemaVersion = previous.SchemaVersion + 2
        }, previous.SchemaVersion));
        var noChange = fixture.Apply(fixture.ConfigureResource(previous with
        { SchemaVersion = previous.SchemaVersion + 1 }, previous.SchemaVersion));
        using var overflowFixture = new ResourcePolicyUpdateFixture(long.MaxValue);
        var maximum = overflowFixture.Resource();
        var overflow = overflowFixture.Apply(overflowFixture.ConfigureResource(maximum with
        {
            FieldPolicies = [new(ResourcePolicyUpdateFixture.SecretField, NewClassification)]
        }, long.MaxValue));

        await Assert.That(ResourcePolicyUpdateFixture.Failure(invalidIncrement).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(ResourcePolicyUpdateFixture.Failure(noChange).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(ResourcePolicyUpdateFixture.Failure(overflow).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(overflowFixture.Resource().SchemaVersion).IsEqualTo(long.MaxValue);
    }

    [Test]
    public async Task AcRpol002RejectsEveryNonPolicyResourceShapeChangeWithoutPartialState()
    {
        using var fixture = new ResourcePolicyUpdateFixture();
        var previous = fixture.Resource();
        ResourceDefinition[] changes =
        [
            previous with { Kind = ResourceKind.Topic },
            previous with { TransactionDomainId = "other-domain" },
            previous with { Indexes = [new("by-public", ["/public"])] },
            previous with { Authority = DocumentAuthority.EventStream },
            previous with { QueuePolicy = previous.QueuePolicy with { MaxAttempts = previous.QueuePolicy.MaxAttempts + 1 } },
            previous with { EventRetention = previous.EventRetention with { MaxEvents = previous.EventRetention.MaxEvents + 1 } },
            previous with { RelationalSchema = new(RelationalMachineKey, [new(RelationalMachineKey, RelationalColumnType.Text)]) },
            previous with { Paused = true }
        ];
        var resourceBytes = fixture.ResourceBytes()!;
        var documentBytes = fixture.DocumentBytes()!;

        foreach (var shape in changes)
        {
            var replacement = shape with
            {
                FieldPolicies = [new(ResourcePolicyUpdateFixture.SecretField, NewClassification)],
                SchemaVersion = previous.SchemaVersion + 1
            };
            var failure = ResourcePolicyUpdateFixture.Failure(fixture.Apply(fixture.ConfigureResource(replacement,
                previous.SchemaVersion)));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            await Assert.That(fixture.ResourceBytes()!.SequenceEqual(resourceBytes)).IsTrue();
            await Assert.That(fixture.DocumentBytes()!.SequenceEqual(documentBytes)).IsTrue();
        }
    }

    [Test]
    public async Task AcRpol002RejectsMalformedPolicyPointerAndLegacyPolicyOverwriteWithoutPartialState()
    {
        using var fixture = new ResourcePolicyUpdateFixture();
        var previous = fixture.Resource();
        var malformed = fixture.Apply(fixture.ConfigureResource(previous with
        {
            FieldPolicies = [new("/secret~2name", NewClassification)],
            SchemaVersion = previous.SchemaVersion + 1
        }, previous.SchemaVersion));
        var legacy = fixture.Apply(fixture.ConfigureResource(previous with
        { FieldPolicies = [new(ResourcePolicyUpdateFixture.SecretField, NewClassification)] }));
        var resourceBytes = fixture.ResourceBytes()!;
        var documentBytes = fixture.DocumentBytes()!;

        await Assert.That(ResourcePolicyUpdateFixture.Failure(malformed).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(ResourcePolicyUpdateFixture.Failure(legacy).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(fixture.ResourceBytes()!.SequenceEqual(resourceBytes)).IsTrue();
        await Assert.That(fixture.DocumentBytes()!.SequenceEqual(documentBytes)).IsTrue();
    }

    [Test]
    public async Task AcRpol002RequiresPersistedAdministratorAuthorityForPolicyCas()
    {
        using var fixture = new ResourcePolicyUpdateFixture();
        var previous = fixture.Resource();
        var writer = new PrincipalRecord("ordinary-writer", ResourcePolicyUpdateFixture.TenantId, [], []);
        fixture.Database.Apply(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal,
            ResourcePolicyUpdateFixture.RootId, TimeProvider.System.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(new ConfigurePrincipalRequest(writer), JsonDefaults.Options)))
            .Get<PrincipalRecord>();
        var replacement = previous with
        {
            FieldPolicies = [new(ResourcePolicyUpdateFixture.SecretField, NewClassification)],
            SchemaVersion = previous.SchemaVersion + 1
        };

        var failure = ResourcePolicyUpdateFixture.Failure(fixture.Apply(fixture.ConfigureResource(replacement,
            previous.SchemaVersion, principalId: writer.Id)));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(JsonData.Fingerprint(fixture.Resource())).IsEqualTo(JsonData.Fingerprint(previous));
    }

    [Test]
    public async Task AcRpol002BlobQuotaIsOutsideThePolicyOnlyReplacementSet()
    {
        var previous = new ResourceDefinition("blob-policy", ResourceKind.BlobStore,
            ResourcePolicyUpdateFixture.DomainId)
        { BlobPolicy = new() };
        var previousPolicy = previous.BlobPolicy!;
        var replacement = previous with
        {
            BlobPolicy = previousPolicy with { MaxBlobBytes = previousPolicy.MaxBlobBytes + 1 },
            SchemaVersion = previous.SchemaVersion + 1
        };

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ResourcePolicyUpdates.Validate(previous, replacement, previous.SchemaVersion));

        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
    }
}
