using System.Reflection;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeContractAttributeTests
{
    [Test]
    public async Task AcIs001EveryFrozenContractAliasHasGeneratedExplicitMemberIds()
    {
        var contracts = typeof(NativeSerialization).Assembly.GetTypes();
        var aliases = typeof(NativeContractAliases).GetFields(BindingFlags.Static | BindingFlags.NonPublic);
        await Assert.That(aliases.Length > 0).IsTrue();
        foreach (var alias in aliases)
        {
            var type = contracts.Single(candidate => candidate.Name.Split('`')[0] == alias.Name);
            await Assert.That(type.IsDefined(typeof(global::Orleans.GenerateSerializerAttribute))).IsTrue();
            var serializedAlias = type.GetCustomAttributesData().Single(attribute => attribute.AttributeType == typeof(global::Orleans.AliasAttribute));
            await Assert.That(serializedAlias.ConstructorArguments[0].Value).IsEqualTo(alias.GetRawConstantValue());
            await AssertMembersAsync(type);
        }
    }

    private static async Task AssertMembersAsync(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(property => property.GetMethod is not null && property.SetMethod is not null).ToArray();
        var identifiers = new List<object?>();
        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributesData().Where(attribute => attribute.AttributeType == typeof(global::Orleans.IdAttribute)).ToArray();
            await Assert.That(attributes.Length).IsEqualTo(1);
            identifiers.Add(attributes.Single().ConstructorArguments[0].Value);
        }
        await Assert.That(identifiers.Distinct().Count()).IsEqualTo(properties.Length);
    }
}
