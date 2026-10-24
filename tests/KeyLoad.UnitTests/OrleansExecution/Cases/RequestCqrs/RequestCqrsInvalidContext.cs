namespace KeyLoad.UnitTests.OrleansExecution;

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
