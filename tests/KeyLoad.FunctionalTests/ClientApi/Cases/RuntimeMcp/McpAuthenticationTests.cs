using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class McpAuthenticationTests
{
    internal const string PrincipalId = "root";
    internal const string SharedValue = "execution-auth-shared-value";
    private const string UnicodePrincipal = "principal-ї-漢-🧭-e\u0301";
    private const string LegacyPrincipalJson = "{\"id\":\"root\"}";
    private static readonly uint[] PrincipalIds = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
    private static readonly uint[] GrantIds = [0, 1, 2];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualPrincipalProjectionMatchesOriginalPublicJsonWithReferences(bool expires)
    {
        using var database = new TestDatabase();
        var stored = Principal(database);
        var shared = new ScopeGrant(SharedValue, SharedValue, Capability.All);
        ImmutableArray<string> repeated = [SharedValue, SharedValue];
        var principal = stored with
        {
            Id = UnicodePrincipal,
            Grants = [shared, shared],
            FieldGrants = repeated,
            Projects = repeated,
            Teams = repeated,
            OwnerId = SharedValue,
            ExpiresAt = expires ? DateTimeOffset.UnixEpoch : null
        };
        var payload = OrleansSerialization.Serialize(new GrainValue(principal));
        var shape = McpAuthentication.Inspect(payload, UnitMcpOptions.Execution(), CancellationToken.None);
        var publicShape = McpFrameBounds.InspectValue(JsonDefaults.Serialize(principal), McpFramingProtocol.MaximumDataReplyBytes, UnitMcpOptions.Execution());
        await Assert.That(shape).IsEqualTo(publicShape);
        var decoded = McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        await Assert.That(ReferenceEquals(decoded.Grants[0], decoded.Grants[1])).IsTrue();
    }

    [Test]
    public async Task PersistedPrincipalAuthenticationOwnsDecodedValues()
    {
        using var database = new TestDatabase();
        var requested = Principal(database) with
        {
            Id = SharedValue,
            ClusterAdministrator = false,
            OwnerId = SharedValue,
            Projects = [SharedValue],
            Teams = [SharedValue]
        };
        var principal = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(requested)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(principal).AsSpan().SequenceEqual(JsonDefaults.Serialize(requested))).IsTrue();
        var expected = JsonDefaults.Serialize(requested);
        var payload = OrleansSerialization.Serialize(new GrainValue(principal));
        using (var state = new McpRequestState(McpFramingProtocol.MaximumDataReplyBytes, UnitMcpOptions.Execution()))
        {
            state.Authenticate(payload, CancellationToken.None);
            await Assert.That(JsonDefaults.Serialize(state.Principal).AsSpan().SequenceEqual(expected)).IsTrue();
            Array.Clear(payload);
            await Assert.That(JsonDefaults.Serialize(state.Principal).AsSpan().SequenceEqual(expected)).IsTrue();
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var fresh = OrleansSerialization.Serialize(new GrainValue(principal));
        using (var denied = new McpRequestState(McpFramingProtocol.MaximumDataReplyBytes, UnitMcpOptions.Execution()))
        {
            Assert.ThrowsExactly<OperationCanceledException>(() => denied.Authenticate(fresh, cancelled.Token));
        }
        using var healthy = new McpRequestState(McpFramingProtocol.MaximumDataReplyBytes, UnitMcpOptions.Execution());
        healthy.Authenticate(fresh, CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(healthy.Principal).AsSpan().SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task CancellationAndLegacyPrincipalJsonDoNotReachTypedAllocation()
    {
        using var database = new TestDatabase();
        var payload = OrleansSerialization.Serialize(new GrainValue(Principal(database)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => McpAuthentication.Inspect(payload, UnitMcpOptions.Execution(), cancelled.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), cancelled.Token));
        var legacy = Encoding.UTF8.GetBytes(LegacyPrincipalJson);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.Inspect(legacy, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task PrincipalReadContractMatchesCurrentGeneratedIdsAndBoundedPublicPropertyNames()
    {
        await AssertIdsAsync(typeof(PrincipalRecord), PrincipalIds);
        await AssertIdsAsync(typeof(ScopeGrant), GrantIds);
        await Assert.That(PrincipalIds.Length).IsEqualTo(McpAuthenticationProjection.PrincipalProperties);
        await Assert.That(GrantIds.Length).IsEqualTo(McpAuthenticationProjection.GrantProperties);
    }

    internal static PrincipalRecord Principal(TestDatabase database)
        => database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(PrincipalId)))!;

    private static async Task AssertIdsAsync(Type type, uint[] expected)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var identifiers = properties.Select(property => property.GetCustomAttribute<global::Orleans.IdAttribute>()!.Id).Order().ToArray();
        await Assert.That(identifiers.AsSpan().SequenceEqual(expected)).IsTrue();
        foreach (var property in properties)
        {
            await Assert.That(Encoding.UTF8.GetByteCount(property.Name)).IsLessThanOrEqualTo(McpFramingProtocol.MaximumPropertyNameBytes);
        }
    }
}
