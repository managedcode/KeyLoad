using System.Security.Cryptography;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class NativeTextCanonicalStateCapture
{
    internal static NativeTextCanonicalState Capture(ZoneTreeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var accumulator = new NativeTextCanonicalStateAccumulator(hash);
        var scan = store.Read(view => view.VisitRange([], NativeTextCrashProtocol.MaximumCanonicalRecords,
            accumulator.Append,
            observer: accumulator.Observe));
        return accumulator.Complete(scan, hash.GetHashAndReset());
    }
}
