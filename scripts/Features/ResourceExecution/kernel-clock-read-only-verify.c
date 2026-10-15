#define _GNU_SOURCE
#include <dlfcn.h>
#include <errno.h>
#include <gnu/libc-version.h>
#include <inttypes.h>
#include <limits.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/timex.h>
#include <time.h>

/* Dedicated Linux prerequisite compatibility observation, never managed-provider qualification. */
static int write_string(const char *text, size_t maximum)
{
    size_t length = strnlen(text, maximum);
    if (length == maximum) return 0;
    putchar('"');
    for (size_t index = 0; index < length; ++index) {
        unsigned char byte = (unsigned char)text[index];
        if (byte < 0x20 || byte > 0x7e) return 0;
        if (byte == '"' || byte == '\\') putchar('\\');
        putchar(byte);
    }
    putchar('"');
    return 1;
}

int main(void)
{
    const uint32_t endian = 1;
    /* Refuse an unexpected native layout before the sole read-only syscall. */
    if (sizeof(void *) != 8 || sizeof(long) != 8 || *(const unsigned char *)&endian != 1 ||
        sizeof(struct timex) != 208 || offsetof(struct timex, modes) != 0 ||
        offsetof(struct timex, maxerror) != 24 || offsetof(struct timex, esterror) != 32 ||
        offsetof(struct timex, status) != 40 || offsetof(struct timex, time.tv_sec) != 72 ||
        offsetof(struct timex, time.tv_usec) != 80 || offsetof(struct timex, tai) != 160 ||
        sizeof(Dl_info) != 32 || offsetof(Dl_info, dli_fname) != 0 ||
        offsetof(Dl_info, dli_fbase) != 8 || offsetof(Dl_info, dli_sname) != 16 ||
        offsetof(Dl_info, dli_saddr) != 24) return 2;
    const char *version = gnu_get_libc_version();
    Dl_info library;
    char path[PATH_MAX];
    if (!version || !dladdr((const void *)version, &library) || !library.dli_fname ||
        !realpath(library.dli_fname, path)) return 3;
    struct timespec started, realtime, finished;
    struct timex value;
    memset(&value, 0, sizeof(value));
    if (clock_gettime(CLOCK_MONOTONIC, &started)) return 4;
    errno = 0;
    /* No input, mode argument or setting-capable entry point exists. All input bytes are zero. */
    int state = adjtimex(&value);
    int native_error = state < 0 ? errno : 0;
    if (clock_gettime(CLOCK_REALTIME, &realtime) || clock_gettime(CLOCK_MONOTONIC, &finished)) return 4;
    printf("{\"schema\":1,\"gnuVersion\":");
    if (!write_string(version, 32)) return 5;
    printf(",\"libcPath\":");
    if (!write_string(path, sizeof(path))) return 5;
    printf(",\"readOnlyModes\":0,\"state\":%d,\"nativeError\":%d,\"status\":%d", state, native_error, value.status);
    printf(",\"maximumErrorMicroseconds\":\"%jd\",\"estimatedErrorMicroseconds\":\"%jd\"",
        (intmax_t)value.maxerror, (intmax_t)value.esterror);
    printf(",\"nativeSeconds\":\"%jd\",\"nativeSubsecond\":\"%jd\"", (intmax_t)value.time.tv_sec, (intmax_t)value.time.tv_usec);
    printf(",\"startedSeconds\":\"%jd\",\"startedNanoseconds\":\"%jd\"", (intmax_t)started.tv_sec, (intmax_t)started.tv_nsec);
    printf(",\"finishedSeconds\":\"%jd\",\"finishedNanoseconds\":\"%jd\"", (intmax_t)finished.tv_sec, (intmax_t)finished.tv_nsec);
    printf(",\"realtimeSeconds\":\"%jd\",\"realtimeNanoseconds\":\"%jd\"}\n", (intmax_t)realtime.tv_sec, (intmax_t)realtime.tv_nsec);
    return ferror(stdout) ? 6 : 0;
}
