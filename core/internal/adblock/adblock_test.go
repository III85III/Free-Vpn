package adblock

import "testing"

func TestDomainBoundaries(t *testing.T) {
	// Blocked ad hosts
	blockedHosts := []string{
		"doubleclick.net",
		"ADS.DoubleClick.NET.",
		"pubads.g.doubleclick.net",
		"pagead2.googlesyndication.com",
		"ad.youtube.com",
		"ads.youtube.com",
		"s.youtube.com",
		"video-ad-stats.googlesyndication.com",
		"adservice.google.com",
		"a.trafficjunky.net",
		"juicyads.com",
		"unityads.unity3d.com",
		"vungle.com",
		"applovin.com",
		"yektanet.com",
		"ua.yektanet.com",
		"api.tapsell.ir",
		"an.facebook.com",
		"pixel.facebook.com",
		"ads.tiktok.com",
	}
	for _, host := range blockedHosts {
		if !Blocked(host) {
			t.Errorf("advertising host should be blocked: %s", host)
		}
	}

	// Legitimate content hosts that must NEVER be blocked
	allowedHosts := []string{
		"youtube.com",
		"www.youtube.com",
		"googlevideo.com",
		"rr1---sn-4g5ednks.googlevideo.com",
		"pornhub.com",
		"accounts.firefox.com",
		"vpn.mozilla.org",
		"notdoubleclick.net",
		"doubleclick.net.example.com",
		"example.com",
		"google.com",
		"github.com",
		"aparat.com",
		"cafebazaar.ir",
		"t.me",
		"instagram.com",
	}
	for _, host := range allowedHosts {
		if Blocked(host) {
			t.Errorf("content or unrelated host should NOT be blocked: %s", host)
		}
	}
}
