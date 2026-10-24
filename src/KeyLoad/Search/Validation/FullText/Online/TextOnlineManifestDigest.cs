using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextOnlineManifestDigest
{
    internal static string Calculate(string path, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> options)
    {
        TextFileIO.VerifyRegularFile(path);
        var size = new FileInfo(path).Length;
        if (size > options.Value.MaximumDiskBytes)
        {
            throw TextErrors.BoundExceeded();
        }
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
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                primary = error;
                throw;
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                primary = error;
                throw;
            }
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            CaptureFailures(primary, error, failures);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            CaptureFailures(primary, error, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return digest ?? throw TextErrors.Corrupt();
    }

    private static void CaptureFailures(Exception? primary, Exception observed, List<Exception> failures)
    {
        if (primary is not null)
        {
            failures.Add(primary);
        }
        if (!ReferenceEquals(primary, observed))
        {
            failures.Add(observed);
        }
    }

}
