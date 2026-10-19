namespace KeyLoad.UnitTests.Features.OrleansExecution;

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
