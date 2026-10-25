using System.Reflection;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class PartitionQueryMcpAssertions
{
    private const string InvalidContract = "The partition-query execution contract is incomplete.";

    internal static async Task VerifyContractsAsync()
    {
        await VerifyAsync(typeof(PartitionQueryRequest), PartitionQueryMcpProtocol.RequestAlias,
            [nameof(PartitionQueryRequest.Version), nameof(PartitionQueryRequest.Partitions),
                nameof(PartitionQueryRequest.Query), nameof(PartitionQueryRequest.Parameters),
                nameof(PartitionQueryRequest.AllowFullScan), nameof(PartitionQueryRequest.AstVersion)]);
        await VerifyAsync(typeof(PartitionQueryRow), PartitionQueryMcpProtocol.RowAlias,
            [nameof(PartitionQueryRow.Reference), nameof(PartitionQueryRow.Row)]);
        await VerifyAsync(typeof(PartitionQueryLeafWitness), PartitionQueryMcpProtocol.WitnessAlias,
            [nameof(PartitionQueryLeafWitness.Partition), nameof(PartitionQueryLeafWitness.CutPosition),
                nameof(PartitionQueryLeafWitness.PolicyEpoch), nameof(PartitionQueryLeafWitness.SchemaVersion),
                nameof(PartitionQueryLeafWitness.AccessPath)]);
        await VerifyAsync(typeof(PartitionQueryPage), PartitionQueryMcpProtocol.PageAlias,
            [nameof(PartitionQueryPage.Version), nameof(PartitionQueryPage.Rows),
                nameof(PartitionQueryPage.Leaves), nameof(PartitionQueryPage.Complete)]);
    }

    private static async Task VerifyAsync(Type type, string alias, string[] names)
    {
        await Assert.That(type.IsDefined(typeof(global::Orleans.GenerateSerializerAttribute))).IsTrue();
        var attribute = type.GetCustomAttributesData().SingleOrDefault(item =>
            item.AttributeType == typeof(global::Orleans.AliasAttribute));
        await Assert.That(attribute is not null).IsTrue();
        await Assert.That(attribute!.ConstructorArguments.Single().Value).IsEqualTo(alias);
        if (type.GetProperties().Length != names.Length)
        {
            throw new InvalidOperationException(InvalidContract);
        }
        for (var index = 0; index < names.Length; index++)
        {
            var property = type.GetProperty(names[index], BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            await Assert.That(property is not null).IsTrue();
            var identifier = property!.GetCustomAttributesData().SingleOrDefault(item =>
                item.AttributeType == typeof(global::Orleans.IdAttribute));
            await Assert.That(identifier is not null).IsTrue();
            await Assert.That(Convert.ToUInt32(identifier!.ConstructorArguments.Single().Value,
                System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo((uint)index);
        }
    }
}
