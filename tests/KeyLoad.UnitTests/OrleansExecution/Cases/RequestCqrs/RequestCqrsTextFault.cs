namespace KeyLoad.UnitTests.OrleansExecution;

internal enum RequestCqrsTextFault
{
    WrongIdentity, UnexpectedPhase, MixedPhase, ExtraCompleted, AfterFinal, FrameBudget
}
