using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal static class AnnSeedValidation
{
    private const string InvalidRequest = "The ANN seed request is invalid.";
    private const string CorruptSource = "The canonical ANN seed source is inconsistent.";
    private const int MaximumIdentifierCharacters = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void ValidateRequest(string principalId, PartitionRef partition,
        string collection, string field, VectorSpace space, AnnSeedWork work)
    {
        if (partition is null || space is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        try
        {
            ValidateIdentifier(principalId, work);
            ValidateIdentifier(partition.TenantId, work);
            ValidateIdentifier(partition.DatabaseId, work);
            ValidateIdentifier(partition.TransactionDomainId, work);
            ValidateIdentifier(partition.PartitionKey, work);
            ValidateIdentifier(collection, work);
            ValidateIdentifier(field, work);
            ValidateIdentifier(space.Id, work);
            ValidateIdentifier(space.Model, work);
            ValidateIdentifier(space.Version, work);
        }
        catch (ArgumentException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }

        if (space.Dimension is < 1 or > 4_096 || !Enum.IsDefined(space.Metric))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
    }

    internal static int Utf8Length(string value)
    {
        try
        {
            return StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
    }

    internal static void ValidateCut(StoreIdentity identity, long position, long applied, OutboxHead head,
        PrincipalRecord principal, ResourceDefinition resource)
    {
        if (identity.NodeId == Guid.Empty || identity.Incarnation == Guid.Empty
            || identity.FormatVersion <= 0 || identity.KeyCodecVersion <= 0 || identity.ReadGeneration < 0
            || position < 0 || applied < 0 || head.Tail < 0 || head.FirstAvailable < 1
            || head.FirstAvailable - 1 > head.Tail || principal.PolicyEpoch < 1 || resource.SchemaVersion < 1)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
        }
    }

    private static void ValidateIdentifier(string value, AnnSeedWork work)
    {
        if (value is null || value.Length > MaximumIdentifierCharacters)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        work.Charge(checked((long)value.Length * 4));
        JsonData.Identifier(value);
        try
        {
            var bytes = StrictUtf8.GetByteCount(value);
            work.Charge(bytes);
            work.Check();
        }
        catch (EncoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
    }
}
