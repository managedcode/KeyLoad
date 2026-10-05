using System.Reflection;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class PartitionQueryMcpNativeAssertions
{
    private const string InvalidContract = "The partition-query native contract is incomplete.";

    internal static async Task VerifyContractsAsync()
    {
        await VerifyAsync(typeof(PartitionQueryRequestV1), PartitionQueryMcpProtocol.RequestAlias,
            [nameof(PartitionQueryRequestV1.Version), nameof(PartitionQueryRequestV1.Partitions),
                nameof(PartitionQueryRequestV1.Query), nameof(PartitionQueryRequestV1.Parameters),
                nameof(PartitionQueryRequestV1.AllowFullScan), nameof(PartitionQueryRequestV1.AstVersion)]);
        await VerifyAsync(typeof(PartitionQueryRowV1), PartitionQueryMcpProtocol.RowAlias,
            [nameof(PartitionQueryRowV1.Reference), nameof(PartitionQueryRowV1.Row)]);
        await VerifyAsync(typeof(PartitionQueryLeafWitnessV1), PartitionQueryMcpProtocol.WitnessAlias,
            [nameof(PartitionQueryLeafWitnessV1.Partition), nameof(PartitionQueryLeafWitnessV1.CutPosition),
                nameof(PartitionQueryLeafWitnessV1.PolicyEpoch), nameof(PartitionQueryLeafWitnessV1.SchemaVersion),
                nameof(PartitionQueryLeafWitnessV1.AccessPath)]);
        await VerifyAsync(typeof(PartitionQueryPageV1), PartitionQueryMcpProtocol.PageAlias,
            [nameof(PartitionQueryPageV1.Version), nameof(PartitionQueryPageV1.Rows),
                nameof(PartitionQueryPageV1.Leaves), nameof(PartitionQueryPageV1.Complete)]);
    }

    private static async Task VerifyAsync(Type type, string alias, string[] names)
    {
        await Assert.That(type.IsDefined(typeof(global::Orleans.GenerateSerializerAttribute))).IsTrue();
        var attribute = type.GetCustomAttributesData().SingleOrDefault(item =>
            item.AttributeType == typeof(global::Orleans.AliasAttribute));
        await Assert.That(attribute is not null).IsTrue();
        await Assert.That(attribute!.ConstructorArguments.Single().Value).IsEqualTo(alias);
        if (type.GetProperties().Length != names.Length)
        { throw new InvalidOperationException(InvalidContract); }
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
