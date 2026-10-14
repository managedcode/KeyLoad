# Native materializer failure recording responsibility

Canonical slice: ClusterReplication. The complete native analyzer inventory measured the materializer at 202 code lines; the earlier manual estimate was not a passing gate.

Its original failure-recording critical section moves to a feature-local lifecycle helper using the same Lock and a ref to the same mutable failure field. Both original catches call it at the original points before their bare rethrow. No failure value is sampled or copied before lock acquisition; worker, waiter wakeup, recovery, apply gate and shutdown ownership remain in the physical owner.

Numeric limits and all recovery/replication acknowledgement contracts stay unchanged. Current-source full compiler and original recovery/RF3 qualification remain required.
