using System.Net;
using System.Text.Json;
using KeyLoad.Comparisons.Targets;
using StackExchange.Redis;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class RedisReplicaDiagnosticTests
{
    private readonly RedisReplicaDiagnostics diagnostics = new(ExecutionPolicyFixture.ReadDiagnostics());

    private const string PrimaryHost = "primary";
    private const string ReplicaHost = "replica1";
    private const string RuntimeHost = "primary.dev.internal";
    private const string Secret = "credential-marker";
    private const string PredicateField = nameof(RedisReplicaDiagnostic.Predicate), RoleCountField = nameof(RedisReplicaDiagnostic.RoleCount);
    private const string PrimaryHostField = nameof(RedisReplicaDiagnostic.PrimaryHost), InfoHostField = nameof(RedisReplicaDiagnostic.InfoHost);
    private const string RoleHostField = nameof(RedisReplicaDiagnostic.RoleHost), InfoPortField = nameof(RedisReplicaDiagnostic.InfoPort);
    private const string RolePortField = nameof(RedisReplicaDiagnostic.RolePort), RoleStateField = nameof(RedisReplicaDiagnostic.RoleState);
    private const string UnrelatedField = "unrelated-secret";
    private const int Port = 6379;

    /// <summary>AC-ISO-003/005/006: retain the exact first failed predicate without changing endpoint identity.</summary>
    [Test]
    [Arguments("InfoRole")]
    [Arguments("InfoLink")]
    [Arguments("InfoHost")]
    [Arguments("InfoPort")]
    [Arguments("RoleCardinality")]
    [Arguments("RoleName")]
    [Arguments("HostAgreement")]
    [Arguments("RolePort")]
    [Arguments("PortAgreement")]
    [Arguments("ConfiguredPrimary")]
    public async Task ProjectsEachOriginalIdentityPredicate(string predicate)
    {
        var (info, role, primary) = MutatePredicate(predicate);
        using var projection = JsonDocument.Parse(diagnostics.Project(new DnsEndPoint(ReplicaHost, Port), primary, info, role));
        await Assert.That(projection.RootElement.GetProperty(PredicateField).GetString()).IsEqualTo(predicate);
        await Assert.That(projection.RootElement.GetProperty(RoleCountField).GetInt32()).IsEqualTo(role.Length);
    }

    private static (Dictionary<string, string> Info, RedisResult[] Role, DnsEndPoint Primary) MutatePredicate(string predicate)
    {
        var info = Info();
        var role = Role();
        var primary = new DnsEndPoint(RuntimeHost, Port);
        switch (predicate)
        {
            case "InfoRole":
                info[RedisProtocol.RoleField] = RedisProtocol.MasterRole;
                break;
            case "InfoLink":
                info[RedisProtocol.LinkField] = "down";
                break;
            case "InfoHost":
                info.Remove(RedisProtocol.MasterHostField);
                break;
            case "InfoPort":
                info[RedisProtocol.MasterPortField] = "invalid";
                break;
            case "RoleCardinality":
                role = [RedisResult.Create((RedisValue)RedisProtocol.ReplicaRole)];
                break;
            case "RoleName":
                role[0] = RedisResult.Create((RedisValue)RedisProtocol.MasterRole);
                break;
            case "HostAgreement":
                role[1] = RedisResult.Create((RedisValue)ReplicaHost);
                break;
            case "RolePort":
                role[2] = RedisResult.Create((RedisValue)"invalid");
                break;
            case "PortAgreement":
                role[2] = RedisResult.Create((RedisValue)(Port + 1));
                break;
            case "ConfiguredPrimary":
                primary = new DnsEndPoint(PrimaryHost, Port);
                break;
        }
        return (info, role, primary);
    }

    [Test]
    public async Task RetainsAliasAndPortsWithoutNormalizingConfiguredIdentity()
    {
        using var projection = JsonDocument.Parse(diagnostics.Project(
            new DnsEndPoint(ReplicaHost, Port), new DnsEndPoint(PrimaryHost, Port), Info(), Role()));
        var root = projection.RootElement;
        await Assert.That(root.GetProperty(PredicateField).GetString()).IsEqualTo("ConfiguredPrimary");
        await Assert.That(root.GetProperty(PrimaryHostField).GetString()).IsEqualTo(PrimaryHost);
        await Assert.That(root.GetProperty(InfoHostField).GetString()).IsEqualTo(RuntimeHost);
        await Assert.That(root.GetProperty(RoleHostField).GetString()).IsEqualTo(RuntimeHost);
        await Assert.That(root.GetProperty(InfoPortField).GetInt32()).IsEqualTo(Port);
        await Assert.That(root.GetProperty(RolePortField).GetInt32()).IsEqualTo(Port);
        await Assert.That(root.GetProperty(RoleStateField).GetString()).IsEqualTo("connected");
    }

    [Test]
    [Arguments("credential-marker")]
    [Arguments("redis://user:credential-marker@primary:6379")]
    [Arguments("primary\ncredential-marker")]
    public async Task UnknownTextAndUnrelatedInfoAreNeverExported(string unsafeValue)
    {
        var info = Info();
        info[RedisProtocol.MasterHostField] = unsafeValue;
        info[RedisProtocol.RoleField] = unsafeValue;
        info[RedisProtocol.LinkField] = unsafeValue;
        info[UnrelatedField] = Secret;
        var role = Role();
        role[0] = RedisResult.Create((RedisValue)unsafeValue);
        role[1] = RedisResult.Create((RedisValue)unsafeValue);
        role[3] = RedisResult.Create((RedisValue)unsafeValue);
        var text = diagnostics.Project(new DnsEndPoint(ReplicaHost, Port), new DnsEndPoint(PrimaryHost, Port), info, role);
        using var projection = JsonDocument.Parse(text);
        await Assert.That(text.Contains(Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Length <= ExecutionPolicyFixture.ReadDiagnostics().Value.RedisReplicaMaximumCharacters).IsTrue();
        await Assert.That(projection.RootElement.GetProperty(InfoHostField).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(projection.RootElement.GetProperty(RoleStateField).ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task OversizedAndInvalidPortsRemainBoundedNullObservations()
    {
        var info = Info();
        info[RedisProtocol.MasterHostField] = new string('x', 8192);
        info[RedisProtocol.MasterPortField] = "65536";
        var role = Role();
        role[1] = RedisResult.Create((RedisValue)new string('x', 8192));
        role[2] = RedisResult.Create((RedisValue)65536);
        var text = diagnostics.Project(new DnsEndPoint(ReplicaHost, Port), new DnsEndPoint(PrimaryHost, Port), info, role);
        using var projection = JsonDocument.Parse(text);
        await Assert.That(text.Length <= ExecutionPolicyFixture.ReadDiagnostics().Value.RedisReplicaMaximumCharacters).IsTrue();
        await Assert.That(projection.RootElement.GetProperty(InfoPortField).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(projection.RootElement.GetProperty(RolePortField).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(projection.RootElement.GetProperty(PredicateField).GetString()).IsEqualTo("ConfiguredPrimary");
    }

    [Test]
    public async Task SuccessfulPredicateAndCaseInsensitiveAgreementAreDiagnosticOnly()
    {
        var info = Info();
        info[RedisProtocol.MasterHostField] = RuntimeHost.ToUpperInvariant();
        using var projection = JsonDocument.Parse(diagnostics.Project(
            new DnsEndPoint(ReplicaHost, Port), new DnsEndPoint(RuntimeHost, Port), info, Role()));
        await Assert.That(projection.RootElement.GetProperty(PredicateField).GetString()).IsEqualTo("None");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task IncompleteRoleArrayNeverReadsMissingEntries(int count)
    {
        using var projection = JsonDocument.Parse(diagnostics.Project(new DnsEndPoint(ReplicaHost, Port),
            new DnsEndPoint(PrimaryHost, Port), Info(), Role()[..count]));
        await Assert.That(projection.RootElement.GetProperty(PredicateField).GetString()).IsEqualTo("RoleCardinality");
        await Assert.That(projection.RootElement.GetProperty(RoleCountField).GetInt32()).IsEqualTo(count);
    }

    [Test]
    public async Task InvalidConfiguredEndpointTextIsRedacted()
    {
        var text = diagnostics.Project(new DnsEndPoint(ReplicaHost, Port),
            new DnsEndPoint("redis://user:credential-marker@primary", Port), Info(), Role());
        using var projection = JsonDocument.Parse(text);
        await Assert.That(text.Contains(Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(projection.RootElement.GetProperty(PrimaryHostField).ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    private static Dictionary<string, string> Info() => new(StringComparer.Ordinal)
    {
        [RedisProtocol.RoleField] = RedisProtocol.ReplicaRole,
        [RedisProtocol.LinkField] = RedisProtocol.LinkUp,
        [RedisProtocol.MasterHostField] = RuntimeHost,
        [RedisProtocol.MasterPortField] = "6379",
    };

    private static RedisResult[] Role() =>
    [
        RedisResult.Create((RedisValue)RedisProtocol.ReplicaRole), RedisResult.Create((RedisValue)RuntimeHost),
        RedisResult.Create((RedisValue)Port), RedisResult.Create((RedisValue)"connected"), RedisResult.Create((RedisValue)1),
    ];
}
