using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private MutationReceipt Upsert(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PutVector vector)
    {
        const int DimensionFirstCount = 1;
        const int DimensionValidationBound = 4_096;
        const string UpsertDetailText = "The vector dimension or values are invalid.";
        const string UpsertKindText = "putVector";

        var resource = Resource(tx, partition, vector.Collection, ResourceKind.Collection);
        Authorization.RequireFieldWrite(principal, resource, vector.Field);
        if (vector.Space.Dimension is < DimensionFirstCount or > DimensionValidationBound || vector.Values.Length != vector.Space.Dimension || vector.Values.Any(v => !float.IsFinite(v)))
        {
            throw Errors.Fail(ErrorCode.Validation, UpsertDetailText);
        }

        JsonData.Identifier(vector.Space.Id);
        JsonData.PathSegments(vector.Field);
        var document = VisibleVertex(tx, principal, new(partition, vector.Collection, vector.Id));
        Authorization.RequireWriteRow(principal, document.Access);
        CheckRevision(document.Revision, vector.ExpectedDocumentRevision);
        PutCanonicalVector(tx, partition, vector, document);
        return new(UpsertKindText, vector.Collection, vector.Id, document.Revision);
    }
}
