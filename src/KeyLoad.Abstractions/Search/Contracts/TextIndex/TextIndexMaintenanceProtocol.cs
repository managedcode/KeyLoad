namespace KeyLoad;

/// <summary>Names the explicit protected maintenance transport independently from read-only index waiting.</summary>
public static class TextIndexMaintenanceProtocol
{
    /// <summary>Shared authenticated maintenance route.</summary>
    public const string Route = "/v1/search/text/maintain";
    /// <summary>On-demand administrator maintenance tool.</summary>
    public const string ToolName = "keyload_search_text_maintain";
    /// <summary>Safe uncertainty detail requiring immutable original child receipt reconciliation.</summary>
    public const string Interrupted = "The text index maintenance outcome is uncertain; reconcile its original canonical checkpoint.";
    /// <summary>Static operation guidance with no private source data.</summary>
    public const string Description = "Maintain one administrator-owned disposable native text generation. Build captures a bounded canonical seed; restore loads the actual generation and incrementally replays its pinned canonical history. Release fences the generation and its pin. Preserve the original commandId and complete request for uncertain checkpoint reconciliation. This operation does not replace text-query authorization or WaitForIndex.";
}
