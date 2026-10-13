namespace KeyLoad.Features.QueryExecution;

internal struct SqlStringContinuationState
{
    internal bool InLineComment { get; set; }
    internal bool HasNewline { get; set; }
}
