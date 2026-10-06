using System.Text;

namespace KeyLoad.Core.Features.Search;

internal static class AnnSeedSourceValidator
{
    private const string CorruptSource = "The canonical ANN seed source is inconsistent.";
    private const int MaximumIdentifierCharacters = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Validate(VectorRecord vector, DocumentRecord document,
        PartitionRef partition, string collection, string field, AnnSeedWork work)
    {
        const int DocumentRevisionValidationBoundary = 1;
        const int DimensionFirstCount = 1;
        const int DimensionValidationBound = 4_096;
        const int RevisionValidationBoundary = 1;

        if (vector.Space is null || vector.Values.IsDefault || vector.DocumentRevision < DocumentRevisionValidationBoundary
            || vector.DocumentId is null || vector.Field is null || vector.Space.Id is null
            || vector.Space.Model is null || vector.Space.Version is null
            || document.Reference is null || document.Reference.Partition is null
            || document.Reference.Collection is null || document.Reference.Id is null
            || vector.Space.Dimension is < DimensionFirstCount or > DimensionValidationBound || !Enum.IsDefined(vector.Space.Metric)
            || vector.Values.Length != vector.Space.Dimension || document.Revision < RevisionValidationBoundary
            || vector.DocumentRevision != document.Revision)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
        }
        ValidateVectorIdentifiers(vector, work);
        ValidateReferenceIdentifiers(document.Reference, work);
        RequireMatches(vector, document.Reference, partition, collection, field, work);
        ValidateComponents(vector, work);
    }

    private static void ValidateVectorIdentifiers(VectorRecord vector, AnnSeedWork work)
    {
        ValidateCanonicalIdentifier(vector.DocumentId, work);
        ValidateCanonicalIdentifier(vector.Field, work);
        ValidateCanonicalIdentifier(vector.Space.Id, work);
        ValidateCanonicalIdentifier(vector.Space.Model, work);
        ValidateCanonicalIdentifier(vector.Space.Version, work);
    }

    private static void ValidateReferenceIdentifiers(EntityRef reference, AnnSeedWork work)
    {
        ValidateCanonicalIdentifier(reference.Id, work);
        ValidateCanonicalIdentifier(reference.Collection, work);
        ValidateCanonicalIdentifier(reference.Partition.TenantId, work);
        ValidateCanonicalIdentifier(reference.Partition.DatabaseId, work);
        ValidateCanonicalIdentifier(reference.Partition.TransactionDomainId, work);
        ValidateCanonicalIdentifier(reference.Partition.PartitionKey, work);
    }

    private static void RequireMatches(VectorRecord vector, EntityRef reference,
        PartitionRef partition, string collection, string field, AnnSeedWork work)
    {
        const int EmptyDocumentIdLength = 0;

        if (vector.DocumentId.Length == EmptyDocumentIdLength || !OrdinalEquals(vector.DocumentId, reference.Id, work)
            || !PartitionMatches(reference.Partition, partition, work)
            || !OrdinalEquals(reference.Collection, collection, work)
            || !OrdinalEquals(vector.Field, field, work))
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
        }
    }

    private static void ValidateComponents(VectorRecord vector, AnnSeedWork work)
    {
        const int IndexInitialValue = 0;

        for (var index = IndexInitialValue; index < vector.Values.Length; index++)
        {
            work.Charge();
            if (!float.IsFinite(vector.Values[index]))
            {
                throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
            }
        }
    }

    internal static bool IdentityEquals(string left, string right, AnnSeedWork work)
    {
        if (left is null || right is null)
        {
            work.Charge();
            return false;
        }
        return OrdinalEquals(left, right, work);
    }

    internal static bool SpaceMatches(VectorSpace left, VectorSpace right, AnnSeedWork work)
    {
        const int KeySpaceComparisonWorkUnits = 3;

        var idMatches = OrdinalEquals(left.Id, right.Id, work);
        var modelMatches = OrdinalEquals(left.Model, right.Model, work);
        var versionMatches = OrdinalEquals(left.Version, right.Version, work);
        work.Charge(KeySpaceComparisonWorkUnits);
        return idMatches && modelMatches && versionMatches
            && left.Dimension == right.Dimension && left.Metric == right.Metric;
    }

    private static bool PartitionMatches(PartitionRef left, PartitionRef right, AnnSeedWork work)
        => IdentityEquals(left.TenantId, right.TenantId, work)
            && IdentityEquals(left.DatabaseId, right.DatabaseId, work)
            && IdentityEquals(left.TransactionDomainId, right.TransactionDomainId, work)
            && IdentityEquals(left.PartitionKey, right.PartitionKey, work);

    private static bool OrdinalEquals(string left, string right, AnnSeedWork work)
    {
        const int IndexInitialValue = 0;

        work.Charge();
        var common = Math.Min(left.Length, right.Length);
        for (var index = IndexInitialValue; index < common; index++)
        {
            work.Charge();
            if (left[index] != right[index])
            {
                return false;
            }
        }
        work.Charge();
        return left.Length == right.Length;
    }

    private static void ValidateCanonicalIdentifier(string value, AnnSeedWork work)
    {
        const int MaximumUtf8BytesPerCharacter = 4;

        if (value is null || value.Length > MaximumIdentifierCharacters)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
        }
        work.Charge(checked((long)value.Length * MaximumUtf8BytesPerCharacter));
        int byteCount;
        try
        {
            JsonData.Identifier(value);
            byteCount = StrictUtf8.GetByteCount(value);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
        }
        catch (EncoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
        }
        work.Charge(byteCount);
        work.Check();
    }
}
