using System.Text;
using KeyLoad.ResourceExecution;

namespace KeyLoad.FunctionalTests.InternalSerialization;

/// <summary>AC-IS-003/007: execution serializers preserve canonical bytes across buffer boundaries.</summary>
internal sealed class SerializationExecutionPolicyTests
{
    [Test]
    [Arguments(4095)]
    [Arguments(4097)]
    [Arguments(4096)]
    public async Task PoolPreservesCanonicalBytesAndOriginalShapeAsync(int boundaryCharacters)
    {
        const string TextKey = "text";
        const string EmptyKey = "empty";
        const string FollowingKey = "other";

        var payload = new string('x', boundaryCharacters - 1) + "😀界λ\"\\\n<&" + new string('y', 8192);
        var expected = new Dictionary<string, string> { [TextKey] = payload, [EmptyKey] = string.Empty };
        var json = JsonDefaults.Serialize(expected);
        var decoded = PooledJsonText.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(json));
        var following = PooledJsonText.Deserialize<Dictionary<string, string>>("{\"other\":\"following\"}");
        await Assert.That(following[FollowingKey]).IsEqualTo("following");
        await Assert.That(JsonDefaults.Serialize(decoded).SequenceEqual(json)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(decoded[TextKey]).SequenceEqual(
            OrleansSerialization.Serialize(payload))).IsTrue();
    }
}
