using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineManifestDigest
{
    internal static string Calculate(string path, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> options)
    {
        NativeTextFileIO.VerifyRegularFile(path);
        var size = new FileInfo(path).Length;
        if (size > options.Value.MaximumDiskBytes)
        { throw NativeTextErrors.BoundExceeded(); }
        budget.ChargeBytes(size);
        string? digest = null;
        var failures = new List<Exception>();
        Exception? primary = null;
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                digest = Convert.ToHexStringLower(SHA256.HashData(input));
                budget.Check();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { primary = error; throw; }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { primary = error; throw; }
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { CaptureFailures(primary, error, failures); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { CaptureFailures(primary, error, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return digest ?? throw NativeTextErrors.Corrupt();
    }

    private static void CaptureFailures(Exception? primary, Exception observed, List<Exception> failures)
    {
        if (primary is not null)
        { failures.Add(primary); }
        if (!ReferenceEquals(primary, observed))
        { failures.Add(observed); }
    }

}
