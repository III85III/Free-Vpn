package desktop

import (
	"bytes"
	"image/png"
	"testing"
)

// Every tray state carries decodable III85III artwork in both formats.
func TestTrayIcons(t *testing.T) {
	for _, name := range []string{iconRunning, iconRunningAsleep, iconSet, iconSetAsleep, iconWaiting, iconError, iconOff} {
		data, err := trayFiles.ReadFile("tray/" + name + ".png")
		if err != nil {
			t.Fatal(err)
		}
		img, err := png.Decode(bytes.NewReader(data))
		if err != nil {
			t.Fatalf("%s: %v", name, err)
		}
		if b := img.Bounds(); b.Dx() != 32 || b.Dy() != 32 {
			t.Fatalf("%s: size %v", name, b)
		}
		visible := false
		for y := 0; y < 32; y++ {
			for x := 0; x < 32; x++ {
				_, _, _, alpha := img.At(x, y).RGBA()
				visible = visible || alpha > 0
			}
		}
		if !visible {
			t.Fatalf("%s: empty artwork", name)
		}
		ico, err := trayFiles.ReadFile("tray/" + name + ".ico")
		if err != nil || len(ico) < 4 || !bytes.Equal(ico[:4], []byte{0, 0, 1, 0}) {
			t.Fatalf("%s: not an ICO file: %v", name, err)
		}
		if !bytes.Equal(ico, AppIconICO) {
			t.Fatalf("%s: expected III85III product icon", name)
		}
		if len(trayIcon(name)) == 0 {
			t.Fatalf("%s: no icon for this system", name)
		}
	}
}
