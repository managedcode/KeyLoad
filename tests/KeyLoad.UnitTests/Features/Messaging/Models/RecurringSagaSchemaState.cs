using System.Collections.Immutable;
using KeyLoad.UnitTests.Features.Authorization;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record RecurringSagaSchemaState(ResourceDefinition Resource, PrincipalRecord Scheduler, PrincipalRecord Reader,
    PrincipalRecord Denied, RecurringScheduleDefinition Definition,
    ConfigureRecurringSchedule ScheduleMutation, CompareExchangeSaga SagaMutation,
    SafeProjectorOperationCommand Seed, ImmutableArray<SafeProjectorOperationCommand> History);
