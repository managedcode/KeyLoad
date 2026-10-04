namespace KeyLoad.Comparisons;

internal enum ComparisonProgressPhase
{
    Oracle,
    Initialize,
    Warmup,
    Prepare,
    Measure,
    Validate,
    Complete
}
