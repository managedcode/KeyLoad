namespace KeyLoad.Server;

/// <summary>Closed owning execution failure boundaries; never caller strings.</summary>
internal enum McpPipelineFailureStage
{
    IncomingAdmission,
    OutgoingOutput,
    IncomingExecution
}
