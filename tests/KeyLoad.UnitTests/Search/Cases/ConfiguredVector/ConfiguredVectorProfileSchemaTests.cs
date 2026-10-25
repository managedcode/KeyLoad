using System.Collections.Immutable;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal sealed class ConfiguredVectorProfileSchemaTests
{
    private const int OverProfileBudget = 33;
    private const string Collection = "profile-schema";
    private const string Duplicate = "duplicate";
    private const string NonCanonical = "noncanonical";
    private const string Model = "model";
    private const string Budget = "budget";
    private const string Missing = "missing";
    private const string NullProfile = "null-profile";
    private const string Dimension = "dimension";
    private const string Metric = "metric";
    private const string IncompatibleKind = "incompatible-kind";
    private const int InvalidDimension = 0;
    private const int InvalidMetric = -1;

    [Test]
    [Arguments(Duplicate, ErrorCode.Validation)]
    [Arguments(NonCanonical, ErrorCode.Validation)]
    [Arguments(Model, ErrorCode.Validation)]
    [Arguments(Budget, ErrorCode.ResourceExhausted)]
    [Arguments(Missing, ErrorCode.Validation)]
    [Arguments(NullProfile, ErrorCode.Validation)]
    [Arguments(Dimension, ErrorCode.Validation)]
    [Arguments(Metric, ErrorCode.Validation)]
    [Arguments(IncompatibleKind, ErrorCode.Validation)]
    public async Task InvalidAuthoritativeProfileCannotCreateResourceAndHealthyDefinitionFollows(string failure, ErrorCode expected)
    {
        using var database = new TestDatabase();
        var profiles = InvalidProfiles(failure);
        var definition = new ResourceDefinition(Collection, failure == IncompatibleKind ? ResourceKind.StreamSet : ResourceKind.Collection, database.Partition.TransactionDomainId)
        {
            VectorProfiles = profiles
        };
        var request = new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition);
        var id = Guid.NewGuid();
        var state = ConfiguredVectorProfileFlow.StateBytes(database, id, global: true);
        var applied = database.Database.LastApplied;
        var beforePosition = database.Store.Position;
        var original = database.Submit(OperationKind.ConfigureResource, request, id: id);
        await Assert.That(Errors.Code(original.Problem).Value).IsEqualTo(expected);

        await Assert.That(original.Problem?.Detail).IsEqualTo(ExpectedDetail(failure));
        await Assert.That(database.Database.LastApplied).IsEqualTo(applied + 1);
        await Assert.That(database.Store.Position).IsEqualTo(beforePosition + 1);
        await Assert.That(ConfiguredVectorProfileFlow.StateBytes(database, id, global: true)).IsEquivalentTo(state, CollectionOrdering.Matching);
        var key = KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId, Collection);
        await Assert.That(database.Store.Read(view => view.GetRecord<ResourceDefinition>(key))).IsNull();
        var position = database.Store.Position;
        var complete = QueueWholeFlowStorage.Bytes(database.Store);
        var replay = database.Submit(OperationKind.ConfigureResource, request, id: id);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(complete, CollectionOrdering.Matching);
        var healthy = request with
        {
            Definition = definition with
            {
                Kind = ResourceKind.Collection,
                VectorProfiles = [new(ConfiguredVectorProfileFlow.Field, ConfiguredVectorProfileFlow.Space)]
            }
        };
        var committed = database.Submit(OperationKind.ConfigureResource, healthy).Get<ResourceDefinition>();
        await Assert.That(OrleansSerialization.Serialize(committed).SequenceEqual(OrleansSerialization.Serialize(healthy.Definition))).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(database.Store.Read(view => view.GetRecord<ResourceDefinition>(key))!)
            .SequenceEqual(OrleansSerialization.Serialize(healthy.Definition))).IsTrue();
    }

    private static string ExpectedDetail(string failure) => failure switch
    {
        Duplicate or Missing or NullProfile or Dimension or Metric or IncompatibleKind => "The configured vector field profile is invalid.",
        NonCanonical => "Field paths must be bounded JSON pointers.",
        Model => "An identifier is missing, too long or contains control characters.",
        Budget => "The configured vector field profiles exceed their budget.",
        _ => throw new InvalidOperationException("The execution schema case is undefined.")
    };

    private static ImmutableArray<VectorFieldProfile> InvalidProfiles(string failure) => failure switch
    {
        Missing => [new(string.Empty, ConfiguredVectorProfileFlow.Space)],
        NullProfile => [null!],
        Dimension => [new(ConfiguredVectorProfileFlow.Field, ConfiguredVectorProfileFlow.Space with { Dimension = InvalidDimension })],
        Metric => [new(ConfiguredVectorProfileFlow.Field, ConfiguredVectorProfileFlow.Space with { Metric = (DistanceMetric)InvalidMetric })],
        IncompatibleKind => [new(ConfiguredVectorProfileFlow.Field, ConfiguredVectorProfileFlow.Space)],
        Duplicate => [new(ConfiguredVectorProfileFlow.Field, ConfiguredVectorProfileFlow.Space),
            new(ConfiguredVectorProfileFlow.Field, ConfiguredVectorProfileFlow.Space)],
        NonCanonical => [new("embedding", ConfiguredVectorProfileFlow.Space)],
        Model => [new(ConfiguredVectorProfileFlow.Field, ConfiguredVectorProfileFlow.Space with { Model = string.Empty })],
        Budget => [.. Enumerable.Range(0, OverProfileBudget).Select(index => new VectorFieldProfile("/field" + index,
            ConfiguredVectorProfileFlow.Space))],
        _ => throw new InvalidOperationException("The execution schema case is undefined.")
    };
}
