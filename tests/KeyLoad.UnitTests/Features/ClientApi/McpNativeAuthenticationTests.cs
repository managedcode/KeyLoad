using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class McpNativeAuthenticationTests
{
    internal const string PrincipalId = "root";
    internal const string SharedValue = "native-auth-shared-value";
    private const int RequestCapacity = 16_384;
    private const string LegacyPrincipalJson = "{\"id\":\"root\"}";
    private static readonly uint[] PrincipalIds = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private static readonly uint[] GrantIds = [0, 1, 2];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualNativePrincipalProjectionMatchesOriginalPublicJsonWithReferences(bool expires)
    {
        using var database = new TestDatabase();
        var stored = Principal(database);
        var shared = new ScopeGrant(SharedValue, SharedValue, Capability.All);
        ImmutableArray<string> repeated = [SharedValue, SharedValue];
        var principal = stored with
        {
            Grants = [shared, shared],
            FieldGrants = repeated,
            Projects = repeated,
            OwnerId = SharedValue,
            ExpiresAt = expires ? DateTimeOffset.UnixEpoch : null
        };
        var payload = NativeSerialization.Serialize(new GrainValue(principal));
        var shape = McpNativeAuthentication.Inspect(payload, CancellationToken.None);
        var publicShape = McpFrameBounds.InspectValue(JsonDefaults.Serialize(principal), McpFramingProtocol.MaximumDataReplyBytes);
        await Assert.That(shape).IsEqualTo(publicShape);
        var decoded = McpNativeAuthentication.ReadPrincipal(payload, CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        await Assert.That(ReferenceEquals(decoded.Grants[0], decoded.Grants[1])).IsTrue();
    }

    [Test]
    public async Task PersistedPrincipalAdmissionOwnsDecodedValuesAndReturnsEveryReservation()
    {
        using var database = new TestDatabase();
        var principal = Principal(database);
        var expected = JsonDefaults.Serialize(principal);
        var payload = NativeSerialization.Serialize(new GrainValue(principal));
        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(limits.DataBytes, limits.ControlBytes, limits.IngressBytes);
        var governor = new HttpAdmissionGovernor();
        using (var state = new McpRequestState(governor, memory, RequestCapacity, CancellationToken.None))
        {
            state.Authenticate(payload, CancellationToken.None);
            Array.Clear(payload);
            await Assert.That(JsonDefaults.Serialize(state.Principal).AsSpan().SequenceEqual(expected)).IsTrue();
        }
        AssertFullPools(memory, limits);
        await Assert.That(governor.Status().Node.ControlCommands).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationAndLegacyPrincipalJsonDoNotReachNativeTypedAllocation()
    {
        using var database = new TestDatabase();
        var payload = NativeSerialization.Serialize(new GrainValue(Principal(database)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => McpNativeAuthentication.Inspect(payload, cancelled.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => McpNativeAuthentication.ReadPrincipal(payload, cancelled.Token));
        var legacy = Encoding.UTF8.GetBytes(LegacyPrincipalJson);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpNativeAuthentication.Inspect(legacy, CancellationToken.None)).Code)
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

    internal static void AssertFullPools(McpMemoryBudget memory, McpMemoryLimits limits)
    {
        using var data = memory.Reserve(McpMemoryLane.Data, limits.DataBytes);
        using var control = memory.Reserve(McpMemoryLane.Control, limits.ControlBytes);
        using var ingress = memory.Reserve(McpMemoryLane.Ingress, limits.IngressBytes);
    }

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
