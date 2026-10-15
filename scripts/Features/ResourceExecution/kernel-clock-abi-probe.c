#define _GNU_SOURCE
#include <dlfcn.h>
#include <errno.h>
#include <gnu/libc-version.h>
#include <limits.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/timex.h>
#include <sys/utsname.h>
#include <unistd.h>

/* ABI producer only. Deliberately never calls adjtimex or any clock-setting API. */
static int write_string(const char *value, size_t maximum)
{
    size_t length = strnlen(value, maximum);
    if (length == maximum) return 0;
    putchar('"');
    for (size_t i = 0; i < length; ++i) {
        unsigned char byte = (unsigned char)value[i];
        if (byte < 0x20 || byte > 0x7e) return 0;
        if (byte == '"' || byte == '\\') putchar('\\');
        putchar(byte);
    }
    putchar('"');
    return 1;
}

int main(void)
{
    int (*observe_signature)(struct timex *) = adjtimex;
    int (*address_signature)(const void *, Dl_info *) = dladdr;
    const char *(*version_signature)(void) = gnu_get_libc_version;
    (void)observe_signature;
    (void)address_signature;
    const char *version = version_signature();
    Dl_info library;
    struct stat identity;
    struct utsname host;
    char path[PATH_MAX];
    const uint32_t endian = 1;
    if (!version || strnlen(version, 32) == 32 ||
        !dladdr((const void *)version, &library) || !library.dli_fname ||
        !realpath(library.dli_fname, path) || stat(path, &identity) || uname(&host)) return 2;
    if (sizeof(void *) != 8 || sizeof(long) != 8 || *(const unsigned char *)&endian != 1 ||
        (strcmp(host.machine, "x86_64") && strcmp(host.machine, "aarch64")) ||
        !S_ISREG(identity.st_mode) || identity.st_size <= 0 || identity.st_size > 16 * 1024 * 1024) return 3;
    printf("{\"schema\":1,\"gnuVersion\":");
    if (!write_string(version, 32)) return 4;
    printf(",\"headerGnuMajor\":%d,\"headerGnuMinor\":%d,\"architecture\":", __GLIBC__, __GLIBC_MINOR__);
    if (!write_string(host.machine, 32)) return 4;
    printf(",\"kernelRelease\":");
    if (!write_string(host.release, sizeof(host.release))) return 4;
    printf(",\"libcPath\":");
    if (!write_string(path, sizeof(path))) return 4;
    printf(",\"libcDevice\":\"%llu\",\"libcInode\":\"%llu\",\"libcBytes\":%lld", (unsigned long long)identity.st_dev,
        (unsigned long long)identity.st_ino, (long long)identity.st_size);
    printf(",\"pointerBytes\":%zu,\"longBytes\":%zu,\"littleEndian\":true", sizeof(void *), sizeof(long));
    printf(",\"timex\":{\"size\":%zu,\"modes\":%zu,\"maximumError\":%zu,\"estimatedError\":%zu,\"status\":%zu,\"seconds\":%zu,\"subsecond\":%zu,\"tai\":%zu}",
        sizeof(struct timex), offsetof(struct timex, modes), offsetof(struct timex, maxerror), offsetof(struct timex, esterror),
        offsetof(struct timex, status), offsetof(struct timex, time.tv_sec), offsetof(struct timex, time.tv_usec), offsetof(struct timex, tai));
    printf(",\"dlInfo\":{\"size\":%zu,\"fileName\":%zu,\"base\":%zu,\"symbolName\":%zu,\"symbolAddress\":%zu}}\n",
        sizeof(Dl_info), offsetof(Dl_info, dli_fname), offsetof(Dl_info, dli_fbase), offsetof(Dl_info, dli_sname), offsetof(Dl_info, dli_saddr));
    return ferror(stdout) ? 5 : 0;
}
