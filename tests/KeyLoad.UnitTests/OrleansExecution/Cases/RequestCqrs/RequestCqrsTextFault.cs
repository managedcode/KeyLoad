namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal enum RequestCqrsTextFault
{
    WrongIdentity, UnexpectedPhase, MixedPhase, ExtraCompleted, AfterFinal, FrameBudget
}
