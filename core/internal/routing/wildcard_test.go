package routing

import "testing"

func TestWildcardDomain(t *testing.T) {
	r, err := parseRule("*.domain.tld")
	if err != nil {
		t.Fatal(err)
	}
	s := newDomainSet()
	if err := s.add(domainRegex, r.value); err != nil {
		t.Fatal(err)
	}
	for host, want := range map[string]bool{"domain.tld": false, "subdomain.domain.tld": true, "a.b.domain.tld": true, "notdomain.tld": false, "domain.tld.evil": false} {
		if s.Contains(host) != want {
			t.Errorf("incorrect match for %s", host)
		}
	}
}
