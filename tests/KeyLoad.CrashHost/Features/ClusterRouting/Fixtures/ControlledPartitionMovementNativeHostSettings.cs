using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

[ConfigurationBinding]
internal sealed partial class ControlledPartitionMovementNativeHostSettings
{
    private const string RootPrefix = "keyload-movement-native-hosts-";
    private const string ClusterPrefix = "movement-native-hosts-";
    private const string NodePrefix = "node";
    private const string NativeGuidFormat = "N";
    private const string IdentityGuidFormat = "D";
    private const string RootCredentialPrefix = "root.";
    private const int SecretBytes = 32;
    private const long InitialEpoch = 1;
    private const int FirstDestination = ControlledPartitionMovementNativeHostEndpoints.GroupSize;
    private const string ControlId = "10213243-5465-7687-98a9-bacbdcedfe0f";
    private const string ControlIncarnation = "00112233-4455-6677-8899-aabbccddeeff";
    private const string DestinationId = "da36582e-c35f-44ec-b0a4-29043539dc5f";
    private const string DestinationIncarnation = "c5d21e44-c4dc-4b21-924f-90b1521cd5e5";
    private readonly string signing;
    private readonly string controlPeer;
    private readonly string destinationPeer;
    private readonly ImmutableArray<string> origins;
    private readonly ImmutableArray<System.Net.IPEndPoint> silos;
    private readonly string cluster;
    private readonly int? maximumBatchBytes;
    private readonly ImmutableArray<string> routingArguments = [];
    internal string AdministratorKey
    {
        get;
    }
    internal string Root
    {
        get;
    }
    internal RegisteredPhysicalOwnerV1 Control
    {
        get;
    }
    internal RegisteredPhysicalOwnerV1 Destination
    {
        get;
    }

    internal ControlledPartitionMovementNativeHostSettings(ControlledPartitionMovementNativeHostEndpoints endpoints,
        int? maximumBatchBytes = null, IOptions<GrainRoutingOptions>? routingOptions = null)
    {
        if (maximumBatchBytes is { } maximum)
        {
            new DatabaseLimits { MaxBatchBytes = maximum }.Validate();
        }
        routingArguments = ControlledPartitionMovementNativeRoutingArguments.Capture(routingOptions);
        this.maximumBatchBytes = maximumBatchBytes;
        origins = Enumerable.Range(FirstSource, ControlledPartitionMovementNativeHostEndpoints.OwnerCount)
            .Select(endpoints.Origin).ToImmutableArray();
        silos = Enumerable.Range(FirstSource, ControlledPartitionMovementNativeHostEndpoints.OwnerCount)
            .Select(endpoints.Silo).ToImmutableArray();
        signing = Secret();
        controlPeer = Secret();
        destinationPeer = Secret();
        cluster = ClusterPrefix + Guid.NewGuid().ToString(NativeGuidFormat);
        AdministratorKey = RootCredentialPrefix + Secret();
        Root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString(NativeGuidFormat));
        Control = Owner(ControlId, ControlIncarnation, FirstSource);
        Destination = Owner(DestinationId, DestinationIncarnation, FirstDestination);
        Directory.CreateDirectory(Root);
    }

    internal string DirectoryPath(int index)
        => Path.Combine(Root, NodePrefix + index.ToString(CultureInfo.InvariantCulture));

    private RegisteredPhysicalOwnerV1 Owner(string id, string incarnation, int first)
    {
        var voters = Enumerable.Range(first, ControlledPartitionMovementNativeHostEndpoints.GroupSize)
            .Select(index => origins[index]).ToImmutableArray();
        return new(new(Guid.Parse(id), Guid.Parse(incarnation), voters, InitialEpoch),
            voters.Select(origin => new Uri(origin).AbsoluteUri).ToImmutableArray());
    }

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
}
