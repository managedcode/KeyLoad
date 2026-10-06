using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaSecurityFixture : IDisposable
{
    internal const string VoterA = "http://voter-a:8080";
    internal const string VoterB = "http://voter-b:8080";
    internal const string VoterC = "http://voter-c:8080";
    internal const string UnknownVoter = "http://unknown-voter:8080";
    internal const string ClusterId = "replica-transport-security";
    internal const string OtherClusterId = "other-replica-cluster";
    internal const string OperationPayload = "{}";
    internal const string DuplicateKind = "duplicate-kind";
    internal const string UnknownKind = "unknown-kind";
    internal const string MissingOperationFields = "missing-operation-fields";
    internal const string InvalidPayload = "invalid-payload";
    internal const string UnknownField = "unknown-field";
    internal const int SiloPort = 11_111;
    internal const int ReservedCapacity = 32;
    internal const int TimeMarginMilliseconds = 1_000;
    internal const int JsonStringQuoteBytes = 2;
    internal const long MaximumClassificationAllocationBytes = 1_048_576;
    internal const char PayloadPadding = 'x';
    internal const byte SnapshotByte = 1;
    internal const byte MutationBit = 1;
    internal static byte[] EmptyRead { get; } = ReplicaProtocolCodec.Serialize(string.Empty);
    private readonly TestDatabase canonical = new();
    private readonly IHost host;
    private readonly ReplicaMessageMac mac;
    private readonly byte[] credential;
    internal KeyLoad.Core.DatabaseEngine Database => canonical.Database;
    internal ReplicaConfiguration Configuration { get; }
    internal ReplicaPeerOptions Options { get; }
    internal ReplicaSiloDiscoveryState Discovery { get; }
    internal ReplicaEnvelopeAuthenticator Sender { get; }
    internal ReplicaEnvelopeAuthenticator Receiver { get; }
    internal string OtherGeneration { get; }

    internal ReplicaSecurityFixture(ReplicaReplayLimits? limits = null)
    {
        host = new HostBuilder().UseOrleans(silo => silo.Services.Configure<EndpointOptions>(endpoint =>
        { endpoint.AdvertisedIPAddress = IPAddress.Loopback; endpoint.SiloPort = SiloPort; endpoint.GatewayPort = 0; })).Build();
        var local = host.Services.GetRequiredService<ILocalSiloDetails>();
        Configuration = new(VoterB, [VoterA, VoterB, VoterC], canonical.Directory, canonical.Store.Identity.Incarnation);
        credential = RandomNumberGenerator.GetBytes(ReplicaTransportProtocol.SecretBytes);
        Options = new(new() { [VoterA] = new(VoterA), [VoterB] = new(VoterB), [VoterC] = new(VoterC) },
            credential, ClusterId)
        { ReplayLimits = limits ?? new() { CriticalPerVoter = ReservedCapacity, ForwardPerVoter = 1, ReadBarrierPerVoter = 1, DataAppendPerVoter = 1 } };
        Discovery = new(Configuration, Options, local);
        Receiver = new(Configuration, Options, Discovery, TimeProvider.System, UnitRoutingOptions.Transport(), UnitRoutingOptions.Replay(), canonicalDatabase: Database);
        var senderConfiguration = Configuration with { LocalId = VoterA };
        Sender = new(senderConfiguration, Options, new(senderConfiguration, Options, local), TimeProvider.System, UnitRoutingOptions.Transport(), UnitRoutingOptions.Replay(), canonicalDatabase: Database);
        OtherGeneration = SiloAddress.New(local.SiloAddress.Endpoint, checked(local.SiloAddress.Generation + 1)).ToParsableString();
        mac = new(Options.Secret, Options.ClusterId);
    }

    internal ReplicaPeerEnvelope Vote() => Request(ReplicaRpc.RequestVote, new VoteRequest(VoterA, 1, 0, 0));
    internal ReplicaPeerEnvelope Read() => Request(ReplicaRpc.ReadBarrier, string.Empty);
    internal ReplicaPeerEnvelope Forward(OperationKind kind) => Request(ReplicaRpc.Forward, Operation(kind));
    internal ReplicaPeerEnvelope Append(params OperationKind?[] kinds) => Request(ReplicaRpc.Append,
        new AppendRequest(VoterA, 1, 0, 0, 0, kinds.Select((kind, index) => new ReplicaEntry(index + 1L, 1,
            kind is { } value ? Operation(value) : null)).ToImmutableArray()));
    internal ReplicaPeerEnvelope Request<T>(ReplicaRpc method, T payload, string? sender = null)
    {
        var request = Sender.SignRequest(VoterB, method, Discovery.RuntimeAddress, ReplicaProtocolCodec.Serialize(payload));
        return sender is null ? request : Resign(request with { Sender = sender });
    }
    internal ReplicaPeerEnvelope Resign(ReplicaPeerEnvelope request) => request with { Signature = mac.Request(request) };
    internal ReplicaPeerEnvelope InvalidControl(string scenario)
    {
        var operation = Operation(OperationKind.Membership);
        var request = Request(ReplicaRpc.Forward, operation);
        return Resign(request with { Payload = ReplicaSecurityWireFixture.Operation(operation, scenario) });
    }
    internal ReplicaPeerEnvelope DuplicateAppend(bool operation)
    {
        var value = new AppendRequest(VoterA, 1, 0, 0, 0, [new(1, 1, Operation(OperationKind.Membership))]);
        var request = Request(ReplicaRpc.Append, value);
        return Resign(request with { Payload = ReplicaSecurityWireFixture.Append(value, operation) });
    }
    private ReplicatedOperation Operation(OperationKind kind)
        => Database.NormalizeOperation(new(Guid.NewGuid(), kind, VoterA, TimeProvider.System.GetUtcNow(), OperationPayload));
    /// <inheritdoc />
    public void Dispose() { Sender.Dispose(); Receiver.Dispose(); mac.Dispose(); host.Dispose(); canonical.Dispose(); CryptographicOperations.ZeroMemory(credential); }
}
