using KeyLoad.Core.Features.CommandOutcomes.Contracts;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class CommandOutcomeScopeSerializationTests
{
    [Test]
    public async Task StableScopeValuesRoundTripThroughGeneratedSerializer()
    {
        var values = new[]
        {
            CommandOutcomeScopeKind.Unknown,
            CommandOutcomeScopeKind.Global,
            CommandOutcomeScopeKind.Partition
        };

        foreach (var value in values)
        {
            var bytes = OrleansSerialization.Serialize(value);
            var restored = OrleansSerialization.Deserialize<CommandOutcomeScopeKind>(bytes);
            await Assert.That((int)restored).IsEqualTo((int)value);
        }
    }
}
