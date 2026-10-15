ARG KEYLOAD_RUNTIME_BASE
FROM ${KEYLOAD_RUNTIME_BASE}
COPY scripts/Features/ResourceExecution/kernel-clock-abi-probe.c /clock-abi-probe.c
# Capture installed libc before tooling; exact matching development headers must remain available.
RUN set -eu; \
    libc_version="$(dpkg-query -W -f='${Version}' libc6)"; \
    libc_path="$(dpkg-query -L libc6 | sed -n '/\/libc\.so\.6$/p')"; \
    test -n "$libc_path"; test "$(printf '%s\n' "$libc_path" | wc -l)" -eq 1; \
    readlink -f "$libc_path" > /libc-path.original; \
    sha256sum "$(cat /libc-path.original)" | cut -d ' ' -f 1 > /libc-sha256.before; \
    printf '%s\n' "$libc_version" > /libc-version.before; \
    getconf GNU_LIBC_VERSION | cut -d ' ' -f 2 > /gnu-version.before; \
    apt-get update; \
    DEBIAN_FRONTEND=noninteractive apt-get install --no-upgrade --no-install-recommends -y gcc "libc6-dev=$libc_version"; \
    dpkg-query -W -f='${Version}\n' libc6 > /libc-version.after; \
    dpkg-query -W -f='${Version}\n' libc6-dev > /libc-dev-version.original; \
    cmp /libc-version.before /libc-dev-version.original; \
    getconf GNU_LIBC_VERSION | cut -d ' ' -f 2 > /gnu-version.after; \
    sha256sum "$(cat /libc-path.original)" | cut -d ' ' -f 1 > /libc-sha256.after; \
    cmp /libc-version.before /libc-version.after; cmp /gnu-version.before /gnu-version.after; \
    cmp /libc-sha256.before /libc-sha256.after; \
    gcc -std=c11 -Wall -Wextra -Werror -O2 /clock-abi-probe.c -ldl -o /clock-abi-probe; \
    /clock-abi-probe > /probe.original.json; \
    rm -rf /var/lib/apt/lists/*
ENTRYPOINT ["/clock-abi-probe"]
