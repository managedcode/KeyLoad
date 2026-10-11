using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal sealed class EventVectorPreparedFirstCleanup : IDisposable
{
    internal EventVectorPreparedFirstCleanup(EventVectorControlPhase original)
    {
        ArgumentNullException.ThrowIfNull(original);
        byte[]? coverage = null;
        byte[]? first = null;
        var transferred = false;
        try
        {
            coverage = original.OriginalCoverageWitness.ToArray();
            first = original.OriginalCapturedFirstOpenPhaseBytes.ToArray();
            Phase = original with { OriginalCoverageWitness = coverage, OriginalCapturedFirstOpenPhaseBytes = first };
            transferred = true;
        }
        finally
        {
            if (!transferred)
            {
                if (coverage is not null)
                { CryptographicOperations.ZeroMemory(coverage); }
                if (first is not null)
                { CryptographicOperations.ZeroMemory(first); }
            }
        }
    }

    internal EventVectorControlPhase Phase { get; }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(Phase.OriginalCoverageWitness).Span);
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(Phase.OriginalCapturedFirstOpenPhaseBytes).Span);
    }
}
