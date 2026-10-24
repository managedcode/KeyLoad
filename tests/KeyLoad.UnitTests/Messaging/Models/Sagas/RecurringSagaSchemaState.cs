using System.Collections.Immutable;
using KeyLoad.UnitTests.Authorization;

namespace KeyLoad.UnitTests.Messaging;

internal sealed record RecurringSagaSchemaState(ResourceDefinition Resource, PrincipalRecord Scheduler, PrincipalRecord Reader,
    PrincipalRecord Denied, RecurringScheduleDefinition Definition,
    ConfigureRecurringSchedule ScheduleMutation, CompareExchangeSaga SagaMutation,
    SafeProjectorOperationCommand Seed, ImmutableArray<SafeProjectorOperationCommand> History);
