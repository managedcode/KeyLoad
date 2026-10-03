using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Closed pinned-DCP alias acceptance data; no native provider or election proof.</summary>
internal sealed class MongoReadinessIdentityTests
{
    private const string Node = "isolated-mongo-1";
    private const string Name = "isolated-mongo-1-00112233445566778899aabbccddeeff";
    private const string Image = "mongo:8.3.9@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ShortId = "aaaaaaaaaaaa";
    private const string NativeAlias = "isolated-mongo-1.dev.internal";
    private const string Network = "aspire-session-network-accepted";
    private const string IdKey = "id";
    private const string NameKey = "name";
    private const string ImageKey = "image";
    private const string RunningKey = "running";
    private const string NetworksKey = "networks";
    private const string AliasesKey = "Aliases";
    private const string LabelsKey = "labels";

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task PinnedDcpResourceMultiplicityAndExactNativeAliasesAreAccepted(int count)
    {
        var aliases = Enumerable.Repeat(Node, count).Concat([NativeAlias, Name, Id, ShortId]).ToArray();
        var actual = Read(aliases);
        actual.RequireNode(Name, Image, Node);
        await Assert.That(actual.Name).IsEqualTo(Name);
        await Assert.That(actual.Networks[Network].SequenceEqual(aliases, StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments("resource")]
    [Arguments("native")]
    [Arguments("name")]
    [Arguments("id")]
    [Arguments("short")]
    [Arguments("foreign")]
    public async Task ExcessMultiplicityAndForeignAliasesAreRejected(string category)
    {
        List<string> aliases =
        [
            Node, NativeAlias, Name, Id, ShortId,
            category switch
            {
                "resource" => Node, "native" => NativeAlias, "name" => Name, "id" => Id,
                "short" => ShortId, _ => "isolated-mongo-2"
            }
        ];
        if (category == "resource")
        {
            aliases.Add(Node);
        }
        var actual = Read([.. aliases]);
        await Assert.That(() => actual.RequireNode(Name, Image, Node)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BothSelectedResourceAndNativeDcpAliasAreRequired(bool omitResource)
    {
        var actual = Read(omitResource ? [NativeAlias, Name] : [Node, Name]);
        await Assert.That(() => actual.RequireNode(Name, Image, Node)).Throws<InvalidOperationException>();
    }

    private static MongoNativeReadinessRegressionIdentity Read(string[] aliases)
    {
        var input = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [IdKey] = Id,
            [NameKey] = "/" + Name,
            [ImageKey] = Image,
            [RunningKey] = true,
            [NetworksKey] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [Network] = new Dictionary<string, object>(StringComparer.Ordinal) { [AliasesKey] = aliases }
            },
            [LabelsKey] = new Dictionary<string, string>(StringComparer.Ordinal)
        };
        return MongoNativeReadinessRegressionIdentity.Parse(JsonSerializer.Serialize(input));
    }
}
