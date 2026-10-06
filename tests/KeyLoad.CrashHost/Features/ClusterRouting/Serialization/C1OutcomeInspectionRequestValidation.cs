using System.Text;
using System.Text.Json;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspectionRequestValidation
{
    private const int FirstPartitionFieldIndex = 0;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] PartitionFields =
    [
        nameof(PartitionRef.TenantId),
        nameof(PartitionRef.DatabaseId),
        nameof(PartitionRef.TransactionDomainId),
        nameof(PartitionRef.PartitionKey)
    ];

    internal static C1OutcomeInspectionRequest Validate(C1OutcomeInspectionRequest? request)
    {
        if (request is null || request.Version != C1OutcomeInspectionProtocol.Version
            || request.ExpectedNodeId == Guid.Empty || request.Incarnation == Guid.Empty || request.CommandId == Guid.Empty
            || request.Directory is null || request.PrincipalId is null || request.Partition is null
            || !ValidDirectory(request.Directory) || !ValidPrincipal(request.PrincipalId)
            || !ValidPartition(request.Partition))
        {
            throw new InvalidDataException(C1OutcomeInspectionProtocol.InvalidRequest);
        }
        return request;
    }

    internal static void ValidatePartitionJsonShape(ref Utf8JsonReader reader,
        Func<InvalidDataException> invalid)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw invalid();
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.ValueIsEscaped)
            {
                throw invalid();
            }
            var name = reader.GetString();
            if (name is null || Array.IndexOf(PartitionFields, name) < FirstPartitionFieldIndex || !seen.Add(name)
                || !reader.Read() || reader.TokenType != JsonTokenType.String || reader.GetString() is null)
            {
                throw invalid();
            }
        }
        if (reader.TokenType != JsonTokenType.EndObject || seen.Count != PartitionFields.Length)
        {
            throw invalid();
        }
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

    private static bool ValidPartition(PartitionRef partition)
        => ValidPartitionComponent(partition.TenantId) && ValidPartitionComponent(partition.DatabaseId)
            && ValidPartitionComponent(partition.TransactionDomainId) && ValidPartitionComponent(partition.PartitionKey);

    private static bool ValidPartitionComponent(string? component)
    {
        const int MinimumPartitionComponentBytes = 1;

        if (component is null)
        {
            return false;
        }
        try
        {
            return StrictUtf8.GetByteCount(component) is >= MinimumPartitionComponentBytes
                and <= C1OutcomeInspectionProtocol.MaximumPartitionComponentBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool ValidPrincipal(string principalId)
    {
        const int GetByteCountEmptyCount = 0;

        if (string.IsNullOrEmpty(principalId))
        {
            return false;
        }
        try
        {
            return StrictUtf8.GetByteCount(principalId) is > GetByteCountEmptyCount and <= C1OutcomeInspectionProtocol.MaximumPrincipalBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
