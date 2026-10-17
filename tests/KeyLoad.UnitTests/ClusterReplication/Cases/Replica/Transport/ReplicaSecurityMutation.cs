namespace KeyLoad.UnitTests;

/// <summary>Cases for authenticated request/reply boundary mutation.</summary>
internal enum ReplicaSecurityMutation
{
    /// <summary>Exact payload bytes.</summary>
    Payload,
    /// <summary>RPC method.</summary>
    Method,
    /// <summary>Request nonce.</summary>
    Nonce,
    /// <summary>Runtime address generation.</summary>
    Generation,
    /// <summary>Durable incarnation.</summary>
    Incarnation,
    /// <summary>Configured voter.</summary>
    Voter,
    /// <summary>Authenticated error code.</summary>
    Error
}
