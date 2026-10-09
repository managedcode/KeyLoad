namespace KeyLoad.Server;

/// <summary>Closed owning native failure boundaries; never caller strings.</summary>
internal enum McpPipelineFailureStage
{
    IncomingAdmission,
    OutgoingNativeOutput
}
