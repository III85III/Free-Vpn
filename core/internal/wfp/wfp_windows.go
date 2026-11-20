package wfp

import (
	"fmt"
	"runtime"
	"unsafe"

	"golang.org/x/sys/windows"
)

// Rules is what Start enforces: on the ALE connect layers (IPv4 and IPv6),
// Programs may connect anywhere, anything may use the TUN interface and
// loopback, and nothing else may reach a remote port 53, nor, with
// BlockIPv6, any IPv6 address.
type Rules struct {
	// Interface is the TUN adapter's interface index.
	Interface uint32
	// Programs are executable paths: the TUN service and the core.
	Programs []string
	// BlockIPv6 is for an adapter without IPv6 (IPv6 turned off on the
	// computer): IPv6 then has no route into it, and must not leave
	// outside it either.
	BlockIPv6 bool
}

// Session holds the filters until Close.
type Session struct {
	engine windows.Handle
}

// A filter transaction can outlive the helper that adds its rules. Keep
// their Go backing buffers and native application IDs until commit/abort.
type filterResources struct {
	roots []any
	blobs []*fwpByteBlob
}

func (r *filterResources) close() {
	for _, blob := range r.blobs {
		freeMemory(unsafe.Pointer(blob))
	}
	runtime.KeepAlive(r.roots)
}

// The filters' weights in their sublayer: the highest matching one
// decides.
const (
	weightPrograms  = 15
	weightLoopback  = 14
	weightInterface = 13
	weightBlockDNS  = 12
	weightBlockIPv6 = 11
)

// Start installs both address families in a single dynamic session. If
// either family fails, closing the session rolls back every added rule.
func Start(r Rules) (s *Session, err error) {
	session := fwpmSession0{
		flags: fwpmSessionFlagDynamic,
	}
	var engine windows.Handle
	if err := call(procFwpmEngineOpen0, 0, rpcCAuthnWinNT, 0, uintptr(unsafe.Pointer(&session)), uintptr(unsafe.Pointer(&engine))); err != nil {
		return nil, fmt.Errorf("opening the filtering engine: %w", err)
	}
	defer func() {
		if err != nil {
			_ = call(procFwpmEngineClose0, uintptr(engine))
		}
	}()
	resources := &filterResources{}
	defer resources.close()
	if err := addFilters(engine, r, resources); err != nil {
		_ = call(procFwpmTransactionAbort0, uintptr(engine))
		return nil, err
	}
	return &Session{engine: engine}, nil
}

// Close removes the filters.
func (s *Session) Close() error {
	if s == nil || s.engine == 0 {
		return nil
	}
	err := call(procFwpmEngineClose0, uintptr(s.engine))
	s.engine = 0
	return err
}

func addFilters(engine windows.Handle, r Rules, resources *filterResources) error {
	key, err := windows.GenerateGUID()
	if err != nil {
		return err
	}
	name, _ := windows.UTF16PtrFromString("III85III VPN TUN")
	description, _ := windows.UTF16PtrFromString("DNS only through the TUN adapter")
	sublayer := fwpmSublayer0{
		subLayerKey: key,
		displayData: fwpmDisplayData0{name: name, description: description},
		weight:      0xffff,
	}
	resources.roots = append(resources.roots, &sublayer)
	if err := call(procFwpmSubLayerAdd0, uintptr(engine), uintptr(unsafe.Pointer(&sublayer)), 0); err != nil {
		return fmt.Errorf("adding the filter sublayer: %w", err)
	}
	type rule struct {
		name          string
		weight        uint8
		action, flags uint32
		conditions    []fwpmFilterCondition0
		ipv6Only      bool
	}
	var rules []rule
	for _, program := range r.Programs {
		blob, err := appID(program)
		if err != nil {
			return fmt.Errorf("the application ID of %s: %w", program, err)
		}
		resources.blobs = append(resources.blobs, blob)
		rules = append(rules, rule{name: "permit " + program, weight: weightPrograms, action: fwpActionPermit, flags: fwpmFilterFlagClearActionRight,
			conditions: []fwpmFilterCondition0{{fieldKey: fwpmConditionALEAppID, matchType: fwpMatchEqual, conditionValue: fwpValue0{typ: fwpByteBlobType, value: uintptr(unsafe.Pointer(blob))}}}})
	}
	rules = append(rules,
		rule{name: "permit loopback", weight: weightLoopback, action: fwpActionPermit,
			conditions: []fwpmFilterCondition0{{fieldKey: fwpmConditionFlags, matchType: fwpMatchFlagsAllSet, conditionValue: fwpValue0{typ: fwpUint32, value: fwpConditionFlagIsLoopback}}}},
		rule{name: "permit the TUN adapter", weight: weightInterface, action: fwpActionPermit,
			conditions: []fwpmFilterCondition0{{fieldKey: fwpmConditionLocalInterfaceIndex, matchType: fwpMatchEqual, conditionValue: fwpValue0{typ: fwpUint32, value: uintptr(r.Interface)}}}},
		rule{name: "block DNS outside the TUN adapter", weight: weightBlockDNS, action: fwpActionBlock,
			conditions: []fwpmFilterCondition0{{fieldKey: fwpmConditionIPRemotePort, matchType: fwpMatchEqual, conditionValue: fwpValue0{typ: fwpUint16, value: 53}}}},
	)
	if r.BlockIPv6 {
		rules = append(rules, rule{name: "block IPv6 outside the TUN adapter", weight: weightBlockIPv6, action: fwpActionBlock, ipv6Only: true})
	}
	for _, layer := range []windows.GUID{fwpmLayerALEAuthConnectV4, fwpmLayerALEAuthConnectV6} {
		if err := call(procFwpmTransactionBegin0, uintptr(engine), 0); err != nil {
			return fmt.Errorf("starting a filter transaction: %w", err)
		}
		for _, item := range rules {
			if item.ipv6Only && layer != fwpmLayerALEAuthConnectV6 {
				continue
			}
			filterKey, err := windows.GenerateGUID()
			if err != nil {
				return err
			}
			filterName, _ := windows.UTF16PtrFromString("III85III VPN TUN: " + item.name)
			filter := &fwpmFilter0{filterKey: filterKey, displayData: fwpmDisplayData0{name: filterName}, flags: item.flags,
				layerKey: layer, subLayerKey: key, weight: fwpValue0{typ: fwpUint8, value: uintptr(item.weight)}, action: fwpmAction0{typ: item.action}}
			if len(item.conditions) > 0 {
				filter.numFilterConditions = uint32(len(item.conditions))
				filter.filterCondition = &item.conditions[0]
			}
			resources.roots = append(resources.roots, filter, item.conditions)
			if err := call(procFwpmFilterAdd0, uintptr(engine), uintptr(unsafe.Pointer(filter)), 0, 0); err != nil {
				return fmt.Errorf("adding the filter %q: %w", item.name, err)
			}
		}
		if err := call(procFwpmTransactionCommit0, uintptr(engine)); err != nil {
			family := "IPv4"
			if layer == fwpmLayerALEAuthConnectV6 { family = "IPv6" }
			return fmt.Errorf("committing %s filters: %w", family, err)
		}
	}
	return nil
}

// appID is the filtering engine's identifier for an executable; free it
// with freeMemory.
func appID(path string) (*fwpByteBlob, error) {
	p, err := windows.UTF16PtrFromString(path)
	if err != nil {
		return nil, err
	}
	var blob *fwpByteBlob
	if err := call(procFwpmGetAppIdFromFileName0, uintptr(unsafe.Pointer(p)), uintptr(unsafe.Pointer(&blob))); err != nil {
		return nil, err
	}
	return blob, nil
}

func freeMemory(p unsafe.Pointer) {
	_, _, _ = procFwpmFreeMemory0.Call(uintptr(unsafe.Pointer(&p)))
}

// call runs a filtering engine function, which returns a Win32 error code.
// Pointer arguments must stay on the heap while lazy DLL loading and the
// Windows RPC call can grow the Go stack or trigger garbage collection.
//
//go:uintptrescapes
func call(proc *windows.LazyProc, args ...uintptr) error {
	if err := proc.Find(); err != nil {
		return err
	}
	r, _, _ := proc.Call(args...)
	if r != 0 {
		return windows.Errno(r)
	}
	return nil
}

var (
	fwpuclnt                      = windows.NewLazySystemDLL("fwpuclnt.dll")
	procFwpmEngineOpen0           = fwpuclnt.NewProc("FwpmEngineOpen0")
	procFwpmEngineClose0          = fwpuclnt.NewProc("FwpmEngineClose0")
	procFwpmTransactionBegin0     = fwpuclnt.NewProc("FwpmTransactionBegin0")
	procFwpmTransactionCommit0    = fwpuclnt.NewProc("FwpmTransactionCommit0")
	procFwpmTransactionAbort0     = fwpuclnt.NewProc("FwpmTransactionAbort0")
	procFwpmSubLayerAdd0          = fwpuclnt.NewProc("FwpmSubLayerAdd0")
	procFwpmFilterAdd0            = fwpuclnt.NewProc("FwpmFilterAdd0")
	procFwpmGetAppIdFromFileName0 = fwpuclnt.NewProc("FwpmGetAppIdFromFileName0")
	procFwpmFreeMemory0           = fwpuclnt.NewProc("FwpmFreeMemory0")
)

// The filtering engine's types (fwptypes.h, fwpmtypes.h), laid out as in C
// on 64-bit Windows.

type fwpByteBlob struct {
	size uint32
	data *uint8
}

type fwpmDisplayData0 struct {
	name, description *uint16
}

type fwpmSession0 struct {
	sessionKey           windows.GUID
	displayData          fwpmDisplayData0
	flags                uint32
	txnWaitTimeoutInMSec uint32
	processID            uint32
	sid                  *windows.SID
	username             *uint16
	kernelMode           int32
}

type fwpmSublayer0 struct {
	subLayerKey  windows.GUID
	displayData  fwpmDisplayData0
	flags        uint32
	providerKey  *windows.GUID
	providerData fwpByteBlob
	weight       uint16
}

// fwpValue0 is FWP_VALUE0 and FWP_CONDITION_VALUE0: a type and an
// 8-byte union, here a number or a pointer.
type fwpValue0 struct {
	typ   uint32
	value uintptr
}

type fwpmFilterCondition0 struct {
	fieldKey       windows.GUID
	matchType      uint32
	conditionValue fwpValue0
}

type fwpmAction0 struct {
	typ        uint32
	filterType windows.GUID
}

type fwpmFilter0 struct {
	filterKey           windows.GUID
	displayData         fwpmDisplayData0
	flags               uint32
	providerKey         *windows.GUID
	providerData        fwpByteBlob
	layerKey            windows.GUID
	subLayerKey         windows.GUID
	weight              fwpValue0
	numFilterConditions uint32
	filterCondition     *fwpmFilterCondition0
	action              fwpmAction0
	// providerContext is the union of rawContext and providerContextKey,
	// 8-byte aligned.
	providerContext [2]uint64
	reserved        *windows.GUID
	filterID        uint64
	effectiveWeight fwpValue0
}

const (
	rpcCAuthnWinNT                 = 10
	fwpmSessionFlagDynamic         = 0x1
	fwpmFilterFlagClearActionRight = 0x8
	fwpActionFlagTerminating       = 0x1000
	fwpActionBlock                 = 0x1 | fwpActionFlagTerminating
	fwpActionPermit                = 0x2 | fwpActionFlagTerminating
	fwpMatchEqual                  = 0
	fwpMatchFlagsAllSet            = 6
	fwpUint8                       = 1
	fwpUint16                      = 2
	fwpUint32                      = 3
	fwpByteBlobType                = 12
	fwpConditionFlagIsLoopback     = 0x1
)

var (
	fwpmLayerALEAuthConnectV4        = windows.GUID{Data1: 0xc38d57d1, Data2: 0x05a7, Data3: 0x4c33, Data4: [8]byte{0x90, 0x4f, 0x7f, 0xbc, 0xee, 0xe6, 0x0e, 0x82}}
	fwpmLayerALEAuthConnectV6        = windows.GUID{Data1: 0x4a72393b, Data2: 0x319f, Data3: 0x44bc, Data4: [8]byte{0x84, 0xc3, 0xba, 0x54, 0xdc, 0xb3, 0xb6, 0xb4}}
	fwpmConditionALEAppID            = windows.GUID{Data1: 0xd78e1e87, Data2: 0x8644, Data3: 0x4ea5, Data4: [8]byte{0x94, 0x37, 0xd8, 0x09, 0xec, 0xef, 0xc9, 0x71}}
	fwpmConditionIPRemotePort        = windows.GUID{Data1: 0xc35a604d, Data2: 0xd22b, Data3: 0x4e1a, Data4: [8]byte{0x91, 0xb4, 0x68, 0xf6, 0x74, 0xee, 0x67, 0x4b}}
	fwpmConditionLocalInterfaceIndex = windows.GUID{Data1: 0x667fd755, Data2: 0xd695, Data3: 0x434a, Data4: [8]byte{0x8a, 0xf5, 0xd3, 0x83, 0x5a, 0x12, 0x59, 0xbc}}
	fwpmConditionFlags               = windows.GUID{Data1: 0x632ce23b, Data2: 0x5167, Data3: 0x435c, Data4: [8]byte{0x86, 0xd7, 0xe9, 0x03, 0x68, 0x4a, 0xa8, 0x0c}}
)
