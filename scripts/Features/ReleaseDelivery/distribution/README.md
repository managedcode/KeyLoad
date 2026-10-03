# KeyLoad Linux x64 distribution

This archive contains the self-contained server and CLI, the exact release version,
and a three-node RF3 Docker Compose configuration. Each node owns a separate
persistent volume; node identities and authorization credentials survive restart.
Release packaging does not establish production, endurance or power-loss qualification.

Verify the published SHA256SUMS before extracting. Docker Compose, Python 3 and
OpenSSL are required for the supplied cluster initializer. Keep the archive in its
own directory, then run:

```sh
./initialize.sh
docker compose up -d
docker compose logs --tail=100
```

The server image reference is the immutable release version. The release also
contains exported server/benchmark image archives that can be loaded with
`docker load --input <archive>` when registry access is unavailable. Public HTTP
ports bind to localhost:5101, :5102 and :5103. Native silo traffic remains inside
the Docker network. An external listener requires an explicitly secured deployment.

Keep `.env` private and preserve it with all three named volumes. Stop and restart
with `docker compose down` and `docker compose up -d`; do not use `down --volumes`
for a database you need to retain. Regenerating cluster identities or mounting one
volume into multiple nodes is unsafe. Follow qualified backup/recovery procedures
for data transfer; a volume copy alone is not a consistency-qualified backup.

The `server/KeyLoad.Server` and `cli/KeyLoad.Cli` executables include their .NET
runtime. The server needs the same RF3 membership and credential settings as the
Compose file. The CLI exposes its actual commands through `cli/KeyLoad.Cli --help`.
The standard CI qualifies the real Docker/Aspire RF3 topology through SDK/MCP;
this packaged Compose deployment has no independent qualification claim.
