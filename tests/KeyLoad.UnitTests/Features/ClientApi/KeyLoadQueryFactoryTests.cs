using KeyLoad.Client;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class KeyLoadQueryFactoryTests
{
    private const string CollectionName = "factory-records";
    private const string DefaultProjectionPath = "*";
    private const string TenantId = "tenant";
    private const string DatabaseId = "database";
    private const string DomainId = "domain";
    private const string PartitionId = "partition";
    private const int DefaultQueryLimit = 100;

    private readonly record struct FactoryRecord(string Name);

    [Test]
    public async Task AcRoc005NonGenericFactoryCreatesTheCanonicalInitialAst()
    {
        var partition = CreatePartition();

        var request = KeyLoadQuery.From<FactoryRecord>(partition, CollectionName).ToRequest();

        await Assert.That(request.Partition).IsEqualTo(partition);
        await Assert.That(request.Query.Collection).IsEqualTo(CollectionName);
        await Assert.That(request.Query.Projection.Length).IsEqualTo(1);
        await Assert.That(request.Query.Projection[0].Path).IsEqualTo(DefaultProjectionPath);
        await Assert.That(request.Query.Order).IsEmpty();
        await Assert.That(request.Query.Filter).IsNull();
        await Assert.That(request.Query.Limit).IsEqualTo(DefaultQueryLimit);
    }

    [Test]
    public async Task AcRoc005FactoryAndExpressionMethodsRejectNullArguments()
    {
        var partition = CreatePartition();
        await Assert.That(Assert.ThrowsExactly<ArgumentNullException>(() =>
            KeyLoadQuery.From<FactoryRecord>(null!, CollectionName)).ParamName).IsEqualTo("partition");
        await Assert.That(Assert.ThrowsExactly<ArgumentNullException>(() =>
            KeyLoadQuery.From<FactoryRecord>(partition, null!)).ParamName).IsEqualTo("collection");

        var query = KeyLoadQuery.From<FactoryRecord>(partition, CollectionName);
        await Assert.That(Assert.ThrowsExactly<ArgumentNullException>(() => query.Where(null!)).ParamName)
            .IsEqualTo("expression");
        await Assert.That(Assert.ThrowsExactly<ArgumentNullException>(() => query.Select<FactoryRecord>(null!)).ParamName)
            .IsEqualTo("expression");
    }

    private static PartitionRef CreatePartition() => new(TenantId, DatabaseId, DomainId, PartitionId);
}
