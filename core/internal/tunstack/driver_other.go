//go:build !linux

package tunstack

// missingDriver: only Linux has a driver that may not be loaded.
func missingDriver(error) bool { return false }

func loadDriver() {}
