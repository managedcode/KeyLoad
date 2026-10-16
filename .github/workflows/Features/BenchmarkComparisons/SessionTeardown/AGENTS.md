# SessionTeardown

Owns the existing job-owned Docker registry shutdown and retention of original native logs/test results. Follow root and workflow policies and ADR-142. Aspire/TUnit owns database startup and disposal; this action performs no per-case reset, measurement, cleanup matrix or report fabrication. Preserve same-job image receipts, failure diagnostics, native test results and least-privileged artifact upload.
