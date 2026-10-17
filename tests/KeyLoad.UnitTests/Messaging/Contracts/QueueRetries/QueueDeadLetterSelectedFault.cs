namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueDeadLetterSelectedFault
{
    IndexAlias,
    MetadataAlias,
    WitnessMetadataAlias,
    MissingBody,
    ForeignBody
}
