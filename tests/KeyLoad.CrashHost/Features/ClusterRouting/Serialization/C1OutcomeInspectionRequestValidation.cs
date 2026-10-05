using System.Text;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspectionRequestValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static C1OutcomeInspectionRequest Validate(C1OutcomeInspectionRequest? request)
    {
        if (request is null || request.Version != C1OutcomeInspectionProtocol.Version
            || request.ExpectedNodeId == Guid.Empty || request.Incarnation == Guid.Empty || request.CommandId == Guid.Empty
            || request.Directory is null || request.PrincipalId is null
            || !ValidDirectory(request.Directory) || !ValidPrincipal(request.PrincipalId))
        {
            throw new InvalidDataException(C1OutcomeInspectionProtocol.InvalidRequest);
        }
        return request;
    }

    private static bool ValidDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
        {
            return false;
        }
        try
        {
            return string.Equals(Path.GetFullPath(directory), directory, StringComparison.Ordinal)
                && Directory.Exists(directory);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool ValidPrincipal(string principalId)
    {
        if (string.IsNullOrEmpty(principalId))
        {
            return false;
        }
        try
        {
            return StrictUtf8.GetByteCount(principalId) is > 0 and <= C1OutcomeInspectionProtocol.MaximumPrincipalBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
