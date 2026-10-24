namespace KeyLoad.UnitTests.Messaging;

internal enum QueueDeadLetterSelectedFault
{
    IndexAlias,
    MetadataAlias,
    WitnessMetadataAlias,
    MissingBody,
    ForeignBody
}
