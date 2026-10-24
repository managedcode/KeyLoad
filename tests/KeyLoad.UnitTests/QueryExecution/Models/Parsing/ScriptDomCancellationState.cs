namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class ScriptDomCancellationState(CancellationTokenSource original)
{
    internal bool Observed
    {
        get; private set;
    }
    internal void OnToken(int index)
    {
        if (index < AdmittedTokenIndex || Observed)
        {
            return;
        }
        Observed = true;
        original.Cancel();
    }
    private const int AdmittedTokenIndex = 2;
}
