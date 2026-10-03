using KeyLoad.Storage;

namespace KeyLoad.Replication;

/// <summary>Defines stable replica format, storage keys, protocol bounds and safe errors.</summary>
public static class ReplicaProtocol
{
    /// <summary>Maximum foreground command deadline.</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    /// <summary>Maximum current-term quorum read deadline.</summary>
    public static readonly TimeSpan ReadBarrierTimeout = TimeSpan.FromSeconds(10);
    /// <summary>Maximum encoded metadata allowance around a replica payload.</summary>
    public const int PayloadMetadataBytes = 65_536;
    /// <summary>Existing authenticated peer identity bound, in UTF16 code units.</summary>
    public const int MaximumIdentityCharacters = 2_048;
    /// <summary>Existing public-safe peer detail bound, in UTF16 code units.</summary>
    public const int MaximumDetailCharacters = 4_096;
    /// <summary>Existing safe rejection detail for a peer payload byte-budget failure.</summary>
    public const string PayloadExceeded = "The replica payload exceeds its transport byte budget.";
    /// <summary>Maximum snapshot chunks sent to one follower in a maintenance round.</summary>
    public const int SnapshotChunksPerRound = 4;
    /// <summary>Persisted replica metadata format.</summary>
    public const int FormatVersion = 2;
    /// <summary>Fixed little-endian replica format fence, identifying native version two.</summary>
    public const ulong PayloadMagic = 0x0032504C52444C4B;
    /// <summary>Number of fixed bytes before every native replica value.</summary>
    public const int PayloadPrefixBytes = sizeof(ulong);
    /// <summary>Safe upgrade detail for legacy or unknown replica encodings.</summary>
    public const string UnsupportedFormat = "The replica format is unsupported. Stop the cluster, preserve the original files and use the documented offline upgrade before starting matching-version voters.";
    /// <summary>Absolute maximum acknowledged snapshot chunk size.</summary>
    public const int MaximumChunkBytes = 1_048_576;
    /// <summary>Safe rejection detail for invalid fixed voter topology.</summary>
    public const string InvalidTopology = "Replication requires distinct fixed voters, a local voter and a nonempty incarnation.";
    /// <summary>Safe rejection detail for invalid timing or byte limits.</summary>
    public const string InvalidLimits = "Replication timing, append and snapshot limits are invalid.";
    /// <summary>Safe failure detail for inconsistent durable replica metadata.</summary>
    public const string CorruptLog = "The durable replica log is inconsistent or corrupt.";
    /// <summary>Safe rejection detail for an invalid ordered suffix.</summary>
    public const string InvalidAppend = "The replica append is not a contiguous, bounded, uncommitted suffix.";
    /// <summary>Safe rejection detail for an invalid image scope, cut or checksum.</summary>
    public const string InvalidSnapshot = "The replica snapshot scope, position or checksum is invalid.";
    /// <summary>Safe rejection detail for a missing, incomplete or fenced transfer.</summary>
    public const string SnapshotUnavailable = "The replica snapshot transfer is absent, incomplete or fenced.";
    /// <summary>Safe retry detail when no current majority-backed leader is available.</summary>
    public const string NoLeader = "The cluster has no current leader with a reachable majority.";
    /// <summary>Safe stable-command retry detail after an uncertain acknowledgement.</summary>
    public const string InterruptedWrite = "Replication was interrupted. Retry the same command ID to resolve its outcome.";
    /// <summary>Node-owned checkpoint subdirectory name.</summary>
    public const string SnapshotDirectory = "snapshots";
    /// <summary>Private incoming transfer descriptor basename.</summary>
    public const string IncomingManifest = "incoming.json";
    /// <summary>Private incoming image basename.</summary>
    public const string IncomingImage = "incoming.snapshot";
    /// <summary>Suffix for unpublished private staging files.</summary>
    public const string TemporarySuffix = ".tmp";
    /// <summary>Encoded durable metadata key component.</summary>
    public const string StateKey = "replica-state";
    /// <summary>Encoded ordered-entry key prefix.</summary>
    public const string EntryKey = "replica-entry";
    /// <summary>Published verified image filename extension.</summary>
    public const string SnapshotExtension = ".snapshot";
    /// <summary>Independent node-owned replica store directory name.</summary>
    public const string ReplicaDirectory = "replica";
    /// <summary>Private local voter identity metadata name.</summary>
    public const string LocalVoter = "local-voter";
    /// <summary>Reserved bootstrap membership directory name.</summary>
    public const string BootstrapDirectory = "bootstrap-membership";
    /// <summary>Only authenticated internal HTTP discovery route.</summary>
    public const string DiscoveryPath = "/internal/silo";
    /// <summary>Safe rejection detail for invalid scope, signature, expiry or replay.</summary>
    public const string InvalidPeer = "The internal replica envelope is invalid, expired or replayed.";
    internal static readonly byte[] StateStorageKey = KeyCodec.Encode(StateKey);

    /// <summary>Creates the stable owned encoded key for one replica entry.</summary>
    /// <param name="index">Ordered replica position.</param>
    /// <returns>Owned encoded storage key bytes.</returns>
    public static byte[] EntryStorageKey(long index) => KeyCodec.Encode(EntryKey, index);
}
