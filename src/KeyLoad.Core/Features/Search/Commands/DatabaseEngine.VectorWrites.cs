using KeyLoad.Core.Features.Search;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private MutationReceipt Upsert(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PutVector vector)
    {
        var resource = Resource(tx, partition, vector.Collection, ResourceKind.Collection);
        Authorization.RequireFieldWrite(principal, resource, vector.Field);
        if (vector.Space.Dimension is < 1 or > 4_096 || vector.Values.Length != vector.Space.Dimension || vector.Values.Any(v => !float.IsFinite(v)))
        {
            throw Errors.Fail(ErrorCode.Validation, "The vector dimension or values are invalid.");
        }

        JsonData.Identifier(vector.Space.Id);
        JsonData.PathSegments(vector.Field);
        var document = VisibleVertex(tx, principal, new(partition, vector.Collection, vector.Id));
        Authorization.RequireWriteRow(principal, document.Access);
        CheckRevision(document.Revision, vector.ExpectedDocumentRevision);
        PutCanonicalVector(tx, partition, vector, document);
        return new("putVector", vector.Collection, vector.Id, document.Revision);
    }
}
