namespace KeyLoad.FunctionalTests.Messaging;

internal enum QueueDeadLetterSelectedFault
{
    IndexAlias,
    MetadataAlias,
    WitnessMetadataAlias,
    MissingBody,
    ForeignBody
}
