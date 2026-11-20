package wfp

import (
	"os"
	"path/filepath"
	"testing"
	"unsafe"

	"golang.org/x/sys/windows"
)

// Explicit opt-in: these brief dynamic sessions exercise the actual Windows
// transaction, and Close removes all of their filters after each iteration.
func TestDynamicFilterSessionReconnect(t *testing.T) {
	if os.Getenv("III85III_TEST_WFP") != "1" || !windows.GetCurrentProcessToken().IsElevated() {
		t.Skip("requires an explicit administrator integration run")
	}
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	for _, group := range []string{"no-programs", "with-programs"} {
		t.Run(group, func(t *testing.T) {
			for i := 0; i < 3; i++ {
				rules := Rules{Interface: 1}
				if group == "with-programs" {
					rules.Programs = []string{exe, filepath.Join(os.Getenv("WINDIR"), "System32", "svchost.exe")}
				}
				session, err := Start(rules)
				if err != nil {
					t.Fatalf("filter transaction %d: %v", i+1, err)
				}
				if err := session.Close(); err != nil {
					t.Fatalf("filter cleanup %d: %v", i+1, err)
				}
				if err := session.Close(); err != nil {
					t.Fatalf("repeated cleanup %d: %v", i+1, err)
				}
			}
		})
	}
}

func TestFilterTransactionStages(t *testing.T) {
	if os.Getenv("III85III_TEST_WFP") != "1" || !windows.GetCurrentProcessToken().IsElevated() {
		t.Skip("requires an explicit administrator integration run")
	}
	for _, stage := range []string{"empty", "sublayer", "loopback", "two-filters", "unique-filters", "both-layers", "interface", "dns-block", "interface-v6", "dns-v6", "mixed-v4", "mixed-v6"} {
		t.Run(stage, func(t *testing.T) {
			name, _ := windows.UTF16PtrFromString("ONYX VPN filter check")
			settings := fwpmSession0{flags: fwpmSessionFlagDynamic}
			var engine windows.Handle
			if err := call(procFwpmEngineOpen0, 0, rpcCAuthnWinNT, 0, uintptr(unsafe.Pointer(&settings)), uintptr(unsafe.Pointer(&engine))); err != nil {
				t.Fatal(err)
			}
			defer call(procFwpmEngineClose0, uintptr(engine))
			if err := call(procFwpmTransactionBegin0, uintptr(engine), 0); err != nil {
				t.Fatal(err)
			}
			defer call(procFwpmTransactionAbort0, uintptr(engine))
			if stage != "empty" {
				key, err := windows.GenerateGUID()
				if err != nil {
					t.Fatal(err)
				}
				layer := fwpmSublayer0{subLayerKey: key, displayData: fwpmDisplayData0{name: name}, weight: 0xffff}
				if err := call(procFwpmSubLayerAdd0, uintptr(engine), uintptr(unsafe.Pointer(&layer)), 0); err != nil {
					t.Fatal(err)
				}
				if stage != "sublayer" {
					condition := fwpmFilterCondition0{fieldKey: fwpmConditionFlags, matchType: fwpMatchFlagsAllSet, conditionValue: fwpValue0{typ: fwpUint32, value: fwpConditionFlagIsLoopback}}
					action := uint32(fwpActionPermit)
					if stage == "interface" || stage == "interface-v6" {
						condition = fwpmFilterCondition0{fieldKey: fwpmConditionLocalInterfaceIndex, matchType: fwpMatchEqual, conditionValue: fwpValue0{typ: fwpUint32, value: 1}}
					}
					if stage == "dns-block" || stage == "dns-v6" {
						condition = fwpmFilterCondition0{fieldKey: fwpmConditionIPRemotePort, matchType: fwpMatchEqual, conditionValue: fwpValue0{typ: fwpUint16, value: 53}}
						action = fwpActionBlock
					}
					filter := fwpmFilter0{displayData: fwpmDisplayData0{name: name}, layerKey: fwpmLayerALEAuthConnectV4, subLayerKey: key, weight: fwpValue0{typ: fwpUint8, value: 1}, action: fwpmAction0{typ: fwpActionPermit}, numFilterConditions: 1, filterCondition: &condition}
					filter.action.typ = action
					if stage == "interface-v6" || stage == "dns-v6" || stage == "mixed-v6" {
						filter.layerKey = fwpmLayerALEAuthConnectV6
					}
					count := 1
					if stage == "two-filters" || stage == "unique-filters" || stage == "both-layers" {
						count = 2
					}
					if stage == "mixed-v4" || stage == "mixed-v6" {
						count = 3
					}
					for i := 0; i < count; i++ {
						if stage == "mixed-v4" || stage == "mixed-v6" {
							filter.weight.value = uintptr(14 - i)
							if i == 1 {
								condition = fwpmFilterCondition0{fieldKey: fwpmConditionLocalInterfaceIndex, matchType: fwpMatchEqual, conditionValue: fwpValue0{typ: fwpUint32, value: 1}}
							}
							if i == 2 {
								condition = fwpmFilterCondition0{fieldKey: fwpmConditionIPRemotePort, matchType: fwpMatchEqual, conditionValue: fwpValue0{typ: fwpUint16, value: 53}}
								filter.action.typ = fwpActionBlock
							}
						}
						if stage == "unique-filters" {
							filter.filterKey, err = windows.GenerateGUID()
							if err != nil {
								t.Fatal(err)
							}
						}
						if stage == "both-layers" && i == 1 {
							filter.layerKey = fwpmLayerALEAuthConnectV6
						}
						if err := call(procFwpmFilterAdd0, uintptr(engine), uintptr(unsafe.Pointer(&filter)), 0, 0); err != nil {
							t.Fatal(err)
						}
					}
				}
			}
			if err := call(procFwpmTransactionCommit0, uintptr(engine)); err != nil {
				t.Fatal(err)
			}
		})
	}
}
