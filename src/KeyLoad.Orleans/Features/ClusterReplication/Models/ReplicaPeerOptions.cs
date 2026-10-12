using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Fixed voter discovery endpoints and cluster peer credentials.</summary>
/// <param name="Endpoints">Configured voter origins used only for authenticated discovery.</param>
/// <param name="Secret">Read-only shared peer HMAC credential.</param>
/// <param name="ClusterId">Shared Orleans cluster identity.</param>
[ConfigurationOptions]
public sealed record ReplicaPeerOptions(Dictionary<string, Uri> Endpoints, ReadOnlyMemory<byte> Secret, string ClusterId)
{
    /// <summary>Discovery connection establishment bound, shorter than the overall RPC deadline.</summary>
    public required TimeSpan ConnectTimeout { get; init; }

    /// <summary>Immutable bounded replay admission pools for each configured voter.</summary>
    public ReplicaReplayLimits ReplayLimits { get; init; } = new();

    /// <summary>Maximum decoded control-command bytes eligible for the reserved consensus nonce pool.</summary>
    public int MaxControlPayloadBytes { get; init; } = new CommandAdmissionLimits().MaxControlPayloadBytes;

    /// <summary>Checks the discovery configuration against the durable replica scope.</summary>
    public void Validate(ReplicaConfiguration configuration)
    {
        const int MaxControlPayloadBytesValidationBoundary = 1;

        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        if (ReplayLimits is null)
        {
            throw new InvalidOperationException(ReplicaTransportProtocol.InvalidReplayLimits);
        }

        ReplayLimits.Validate(configuration.VoterIds.Length);
        if (Secret is not { Length: ReplicaTransportProtocol.SecretBytes }
            || string.IsNullOrWhiteSpace(ClusterId) || ClusterId.Length > ReplicaTransportProtocol.MaximumClusterCharacters
            || MaxControlPayloadBytes < MaxControlPayloadBytesValidationBoundary || MaxControlPayloadBytes > configuration.MaxAppendBytes
            || configuration.MaxAppendBytes > int.MaxValue - ReplicaTransportProtocol.MaximumMetadataBytes
                - ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes
            || configuration.VoterIds.Any(voter => voter.Length > ReplicaTransportProtocol.MaximumIdentityCharacters))
        {
            throw new InvalidOperationException(ReplicaTransportProtocol.InvalidOptions);
        }
    }

}
