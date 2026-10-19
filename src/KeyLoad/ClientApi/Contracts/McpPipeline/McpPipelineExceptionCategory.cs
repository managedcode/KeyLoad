namespace KeyLoad.Server;

/// <summary>Closed categories for a true owning execution failure; never arbitrary exception type text.</summary>
internal enum McpPipelineExceptionCategory
{
    Other,
    KeyLoad,
    InvalidOperation,
    Cancelled
}
