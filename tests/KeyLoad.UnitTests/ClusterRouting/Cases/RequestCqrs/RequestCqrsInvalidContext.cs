namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal enum RequestCqrsInvalidContext
{
    MissingState,
    MismatchedRequest,
    MismatchedCommand,
    MissingPrincipal,
    ForgedSubject,
    ExtraClaim,
    SecondIdentity,
    WrongAuthenticationType
}
