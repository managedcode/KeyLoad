namespace KeyLoad.FunctionalTests.OrleansExecution;

internal enum RequestCqrsTextFault
{
    WrongIdentity, UnexpectedPhase, MixedPhase, ExtraCompleted, AfterFinal, FrameBudget
}
