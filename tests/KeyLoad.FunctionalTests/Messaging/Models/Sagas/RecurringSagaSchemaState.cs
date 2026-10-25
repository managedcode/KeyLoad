using System.Collections.Immutable;
using KeyLoad.FunctionalTests.Authorization;

namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record RecurringSagaSchemaState(ResourceDefinition Resource, PrincipalRecord Scheduler, PrincipalRecord Reader,
    PrincipalRecord Denied, RecurringScheduleDefinition Definition,
    ConfigureRecurringSchedule ScheduleMutation, CompareExchangeSaga SagaMutation,
    SafeProjectorOperationCommand Seed, ImmutableArray<SafeProjectorOperationCommand> History);
