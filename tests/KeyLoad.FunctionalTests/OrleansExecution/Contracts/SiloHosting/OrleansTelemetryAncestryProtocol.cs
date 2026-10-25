namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class OrleansTelemetryAncestryProtocol
{
    internal const string Collection = "telemetry-ancestry-documents";
    internal const string Principal = "telemetry-ancestry-writer";
    internal const string Json = "{\"ancestry\":true}";
    internal const string LocalOperation = "telemetry-local-parent";
    internal const string DeepDocument = "deep";
    internal const string BoundaryDocument = "boundary";
    internal const string HealthyDocument = "healthy";
    internal const string PutKind = "putDocument";
    internal const string Root = "root";
    internal const string MissingParent = "The real telemetry parent activity was not created.";
    internal const string MissingReceipt = "The real operation returned no receipt.";
    internal const int MaximumParentLinks = 32;
    internal const int OneLink = 1;
    internal const int NoLocalParents = 0;
    internal const int PlacementVersion = 1;
    internal const int MaximumSelectedSpans = 64;
    internal const long FirstRevision = 1;
}
