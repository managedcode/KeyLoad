namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Names concrete invalid HTTP header conditions.</summary>
internal enum McpTransportHeaderFault
{
    MissingRevision, EmptyRevision, LegacyRevision, RevisionWhitespace, MarkerRevision, DuplicateRevision,
    Session, EmptySession, LastEvent, EmptyLastEvent, DuplicateMethod, DuplicateName, EmptyMethod, EmptyName,
    EmptyMethodValues, EmptyNameValues, NonAsciiMethod, NonAsciiName, ControlMethod, ControlName, DeleteMethod, DeleteName
}
