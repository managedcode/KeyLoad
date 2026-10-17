namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal enum RequestCqrsTextFault
{
    WrongIdentity, UnexpectedPhase, MixedPhase, ExtraCompleted, AfterFinal, FrameBudget
}
