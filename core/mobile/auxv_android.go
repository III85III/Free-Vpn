//go:build android

package mobile

/*
#include <sys/prctl.h>

static int wasDumpable = -1;

// Runs before the Go runtime starts in this library (its own constructor has
// no priority, so it comes later).
__attribute__((constructor(101), used))
static void openAuxv(void) {
	wasDumpable = prctl(PR_GET_DUMPABLE);
	if (wasDumpable == 0) {
		prctl(PR_SET_DUMPABLE, 1);
	}
}

static void closeAuxv(void) {
	if (wasDumpable == 0) {
		prctl(PR_SET_DUMPABLE, 0);
	}
}
*/
import "C"

// The Go runtime learns what the processor can do from the auxiliary vector.
// Loaded as a library it reads /proc/self/auxv, which Android keeps from a
// process that is not dumpable, as release apps are: Go then takes the
// processor to have no AES instructions, and decrypts every byte of the VPN
// in slow generic code. So the process is dumpable only while the runtime
// starts.
func init() { C.closeAuxv() }
