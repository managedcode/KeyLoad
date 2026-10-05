using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CommandOutcomeScopeNativeSerializationTests
{
    [Test]
    public async Task StableScopeValuesRoundTripThroughGeneratedNativeSerializer()
    {
        var values = new[]
        {
            CommandOutcomeScopeKind.Unknown,
            CommandOutcomeScopeKind.Global,
            CommandOutcomeScopeKind.Partition
        };

        foreach (var value in values)
        {
            var bytes = NativeSerialization.Serialize(value);
            var restored = NativeSerialization.Deserialize<CommandOutcomeScopeKind>(bytes);
            await Assert.That((int)restored).IsEqualTo((int)value);
        }
    }
}
