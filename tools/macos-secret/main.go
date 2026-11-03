// Generate a dashboard password hash, discarding the high-entropy password.
// Only a native one-use local login link can open the app's web interface.
package main

import (
    "crypto/rand"
    "encoding/base64"
    "encoding/json"
    "os"

    "golang.org/x/crypto/bcrypt"
)

func main() {
    raw := make([]byte, 32)
    if _, err := rand.Read(raw); err != nil { panic(err) }
    hash, err := bcrypt.GenerateFromPassword([]byte(base64.RawURLEncoding.EncodeToString(raw)), bcrypt.DefaultCost)
    if err != nil { panic(err) }
    if err := json.NewEncoder(os.Stdout).Encode(map[string]string{"password_hash": string(hash)}); err != nil { panic(err) }
}
