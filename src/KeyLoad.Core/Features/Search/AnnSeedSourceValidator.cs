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
        if (vector.Space is null || vector.Values.IsDefault || vector.DocumentRevision < 1
            || vector.DocumentId is null || vector.Field is null || vector.Space.Id is null
            || vector.Space.Model is null || vector.Space.Version is null
            || document.Reference is null || document.Reference.Partition is null
            || document.Reference.Collection is null || document.Reference.Id is null
            || vector.Space.Dimension is < 1 or > 4_096 || !Enum.IsDefined(vector.Space.Metric)
            || vector.Values.Length != vector.Space.Dimension || document.Revision < 1
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
        if (vector.DocumentId.Length == 0 || !OrdinalEquals(vector.DocumentId, reference.Id, work)
            || !PartitionMatches(reference.Partition, partition, work)
            || !OrdinalEquals(reference.Collection, collection, work)
            || !OrdinalEquals(vector.Field, field, work))
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
        }
    }

    private static void ValidateComponents(VectorRecord vector, AnnSeedWork work)
    {
        for (var index = 0; index < vector.Values.Length; index++)
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
        var idMatches = OrdinalEquals(left.Id, right.Id, work);
        var modelMatches = OrdinalEquals(left.Model, right.Model, work);
        var versionMatches = OrdinalEquals(left.Version, right.Version, work);
        work.Charge(3);
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
        work.Charge();
        var common = Math.Min(left.Length, right.Length);
        for (var index = 0; index < common; index++)
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
        if (value is null || value.Length > MaximumIdentifierCharacters)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
        }
        work.Charge(checked((long)value.Length * 4));
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
