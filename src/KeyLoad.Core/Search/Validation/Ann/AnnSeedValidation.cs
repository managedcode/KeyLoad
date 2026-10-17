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
        const int DimensionFirstCount = 1;
        const int DimensionValidationBound = 4_096;

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

        if (space.Dimension is < DimensionFirstCount or > DimensionValidationBound || !Enum.IsDefined(space.Metric))
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
        const int FormatVersionValidationBoundary = 0;
        const int KeyCodecVersionValidationBoundary = 0;
        const int ReadGenerationValidationBoundary = 0;
        const int PositionValidationBoundary = 0;
        const int AppliedValidationBoundary = 0;
        const int TailValidationBoundary = 0;
        const int FirstAvailableValidationBoundary = 1;
        const int FirstAvailableStep = 1;
        const int PolicyEpochValidationBoundary = 1;
        const int SchemaVersionValidationBoundary = 1;

        if (identity.NodeId == Guid.Empty || identity.Incarnation == Guid.Empty
            || identity.FormatVersion <= FormatVersionValidationBoundary || identity.KeyCodecVersion <= KeyCodecVersionValidationBoundary || identity.ReadGeneration < ReadGenerationValidationBoundary
            || position < PositionValidationBoundary || applied < AppliedValidationBoundary || head.Tail < TailValidationBoundary || head.FirstAvailable < FirstAvailableValidationBoundary
            || head.FirstAvailable - FirstAvailableStep > head.Tail || principal.PolicyEpoch < PolicyEpochValidationBoundary || resource.SchemaVersion < SchemaVersionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
        }
    }

    private static void ValidateIdentifier(string value, AnnSeedWork work)
    {
        const int MaximumUtf8BytesPerCharacter = 4;

        if (value is null || value.Length > MaximumIdentifierCharacters)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        work.Charge(checked((long)value.Length * MaximumUtf8BytesPerCharacter));
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
