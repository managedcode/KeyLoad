namespace KeyLoad.Query.Search;

/// <summary>Identifier-only execution acquisition scope, without query coordinates or caller authority.</summary>
internal readonly record struct AnnProjectionSelection(PartitionRef Partition, string Collection,
    string VectorField, VectorSpace Space, ProjectionConsumerRef Consumer, long IndexGeneration);
