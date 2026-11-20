package fxa

import (
	"context"
	"testing"
	"time"
)

func TestHumanVerificationHasSeparateTimeBudget(t *testing.T) {
	ctx := context.Background()
	if interactiveChallengeTimeout(ctx) != fastlySolveTimeout {
		t.Fatal("background requests must keep their existing deadline")
	}
	ctx = WithCaptchaSolver(ctx, func(context.Context, string) (string, error) {
		return "", nil
	})
	if interactiveChallengeTimeout(ctx) != 5*time.Minute {
		t.Fatal("human verification must have time for user input")
	}
	parent, stop := context.WithTimeout(ctx, time.Second)
	defer stop()
	child, cancel := context.WithTimeout(parent, interactiveChallengeTimeout(parent))
	defer cancel()
	parentDeadline, _ := parent.Deadline()
	childDeadline, _ := child.Deadline()
	if !childDeadline.Equal(parentDeadline) {
		t.Fatal("verification must still respect the parent sign-in deadline")
	}
}
