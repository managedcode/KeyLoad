# KeyLoad delete fixture outbox accounting

TASK-NGR-D1R / REQ-NGR-005 / AC-NGR-005 is read-only source diagnosis.
[The genuine failed job](https://github.com/managedcode/KeyLoad/actions/runs/37132491118/job/111232263528)
uses source `7d1196db51f682f64ffcf4560e7b3f12b49b499b`; its
[original receipt](native-keyload-failures-7d1196.json) retains provider/upload,
ZIP/report/raw hashes, all case counts and the redacted numeric observation.
No implementation, quota, retention or measurement change is delivered here.

The observed head has100000 retained entries,192581709 bytes and no active
consumer. The exact source's default bounds are100000 entries and1073741824
bytes. [AppendOutbox](https://github.com/managedcode/KeyLoad/blob/7d1196db51f682f64ffcf4560e7b3f12b49b499b/src/KeyLoad.Core/ProjectionOutbox.cs)
rejects the next ordinary append when the retained count reaches its cap.
Only projection progress uses the separate reserve. This supports record-cap
exhaustion strongly; the original inner exception and rejected ordinal were not
exported and remain unproven runtime details.

The exact4096-document,10000-operation,256-warmup,five-repetition,16-worker
source yields the following independent entry accounting:

|Source work|Outbox entries|
|---|---:|
|4096 document puts and4096 vector puts|8192|
|256 graph vertices with3 outgoing edges|768|
|4096 event appends|4096|
|Successful mutation-probe effects|4|
|Each completed delete repetition:256 preparation puts,256 warmup deletes,10000 measured-input puts and10000 measured deletes|20512|
|Initial seed/probe plus four completed repetitions|95108|
|Fifth repetition's preparation/warmup before measured-input puts|512|
|Head predicted before fifth measured-input preparation|95620|
|Complete unchanged five-repetition fixture requirement|115620|

The source therefore predicts4380 successful preparation appends to reach100000,
then rejection before timed samples are allocated. Four original repetitions
have10000 successes/zero failures/10000 samples each; the fifth has
ResourceExhausted/null measurement/zero samples, consistent with that prediction.
This arithmetic does not prove the hidden runtime ordinal. Source review compares
18 inspected owning files byte-for-byte with the failed commit and current tree.
Analysis SHA-256 `2c21daf4fde8dfc5a26fe621760e5f112b8e75103b8fc6b5f1c45dc27cf0c7bc`;
source/evidence inventory SHA-256
`9cbce0b5d23dd1cfdb4cccb249c5abfe7eeccf467a603fd94376e6a33069c496`.

The actual server PartitionHost constructs DatabaseEngine with default limits;
NodeOptions and isolated AppHost currently expose no database-limit seam.
Direct Core constructor injection does not configure the real server fixture.
The115620-record minimum is a requirement for this fixture;120000 is only a
bounded candidate, with the byte cap unchanged. A separate implementation
contract must freeze effective limits on every replica, provenance, restart
compatibility, admission, regression boundaries and actual default/candidate
native proof before introducing a server setting. ADR-068 stageE authorizes
observation only. No automatic quota growth, history purge or write retry follows
from this review. The mandatory100k/1m/5m corpus and100k-operation cells require
their own checked capacity and retained-byte accounting; this small control
fixture cannot qualify those scales, the complete cohort or website metrics.
