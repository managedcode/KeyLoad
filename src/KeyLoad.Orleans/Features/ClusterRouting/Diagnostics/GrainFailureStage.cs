namespace KeyLoad.Orleans;

/// <summary>Closed internal stages for locating rejected Orleans database requests.</summary>
internal enum GrainFailureStage
{
    /// <summary>Request envelope authentication or scope validation failed.</summary>
    EnvelopeVerification,
    /// <summary>Encoded request payload was not valid JSON.</summary>
    PayloadSyntax,
    /// <summary>JSON could not be decoded into the operation's typed payload.</summary>
    TypedPayloadDecode,
    /// <summary>An operation requiring a null payload received another JSON shape.</summary>
    RequiredNullPayload,
    /// <summary>Atomic partition resolution rejected the request.</summary>
    PartitionResolution,
    /// <summary>The operation could not establish a quorum read barrier.</summary>
    QuorumRead,
    /// <summary>Persisted authorization rejected the request.</summary>
    Authorization,
    /// <summary>Capability execution rejected or failed the operation.</summary>
    CapabilityExecution,
    /// <summary>Successful capability output could not be encoded as a reply.</summary>
    ReplyEncoding
}
