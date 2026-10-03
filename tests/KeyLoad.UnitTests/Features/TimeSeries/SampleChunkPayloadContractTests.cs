using System.Globalization;
using System.Reflection;
using KeyLoad.Core.Features.TimeSeries;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkPayloadContractTests
{
    private const string ExpectedAlias = "keyload.core.v1.SampleChunkPayload";

    [Test]
    public async Task AcChunk004PayloadAliasAndFieldIdsRemainFrozen()
    {
        var type = typeof(SampleChunkPayload);
        var alias = type.GetCustomAttributesData()
            .Single(attribute => attribute.AttributeType == typeof(global::Orleans.AliasAttribute));
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .OrderBy(property => Convert.ToUInt32(property.GetCustomAttributesData()
                .Single(attribute => attribute.AttributeType == typeof(global::Orleans.IdAttribute))
                .ConstructorArguments[0].Value, CultureInfo.InvariantCulture))
            .ToArray();
        var ids = properties.Select(property => Convert.ToUInt32(property.GetCustomAttributesData()
                .Single(attribute => attribute.AttributeType == typeof(global::Orleans.IdAttribute))
                .ConstructorArguments[0].Value, CultureInfo.InvariantCulture))
            .ToArray();
        var members = properties.Select(property => property.Name).ToArray();
        var types = properties.Select(property => property.PropertyType).ToArray();

        await Assert.That(type.IsDefined(typeof(global::Orleans.GenerateSerializerAttribute))).IsTrue();
        await Assert.That(alias.ConstructorArguments[0].Value).IsEqualTo(ExpectedAlias);
        await Assert.That(ids).IsEquivalentTo(new uint[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, CollectionOrdering.Matching);
        await Assert.That(members).IsEquivalentTo(
            new[] { "FormatVersion", "RecordCount", "UtcTicks", "Offsets", "Sequences", "Values", "Series", "EventIds", "Tags", "Checksum" },
            CollectionOrdering.Matching);
        await Assert.That(types).IsEquivalentTo(new[]
        {
            typeof(int), typeof(int), typeof(ReadOnlyMemory<byte>), typeof(ReadOnlyMemory<byte>),
            typeof(ReadOnlyMemory<byte>), typeof(ReadOnlyMemory<byte>), typeof(ReadOnlyMemory<byte>),
            typeof(ReadOnlyMemory<byte>), typeof(ReadOnlyMemory<byte>), typeof(ReadOnlyMemory<byte>)
        }, CollectionOrdering.Matching);
    }
}
