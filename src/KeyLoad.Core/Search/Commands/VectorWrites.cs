using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes VectorWrites operations against the caller's database context.</summary>
public static class VectorWrites
{
    internal static MutationReceipt Upsert(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PutVector vector)
    {
        const int DimensionFirstCount = 1;
        const int DimensionValidationBound = 4_096;
        const string UpsertDetailText = "The vector dimension or values are invalid.";
        const string UpsertKindText = "putVector";
        var resource = engine.Resource(tx, partition, vector.Collection, ResourceKind.Collection);
        engine.Authorization.RequireFieldWrite(principal, resource, vector.Field);
        if (vector.Space.Dimension is < DimensionFirstCount or > DimensionValidationBound || vector.Values.Length != vector.Space.Dimension || vector.Values.Any(v => !float.IsFinite(v)))
        {
            throw Errors.Fail(ErrorCode.Validation, UpsertDetailText);
        }

        JsonData.Identifier(vector.Space.Id);
        JsonData.PathSegments(vector.Field);
        var document = engine.VisibleVertex(tx, principal, new(partition, vector.Collection, vector.Id));
        engine.Authorization.RequireWriteRow(principal, document.Access);
        DocumentStorageExecutionDocuments.CheckRevision(document.Revision, vector.ExpectedDocumentRevision);
        Search.VectorFieldProfiles.Require(resource, vector);
        VectorProjectionApply.PutCanonicalVector(tx, partition, vector, document);
        return new(UpsertKindText, vector.Collection, vector.Id, document.Revision);
    }
}
