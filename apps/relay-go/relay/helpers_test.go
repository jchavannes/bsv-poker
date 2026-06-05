package relay

import (
	"encoding/json"
	"io"
	"strings"
	"testing"
)

// jsonBody wraps a literal JSON string as a request body reader.
func jsonBody(s string) io.Reader { return strings.NewReader(s) }

// mustJSON unmarshals body into v, failing the test on error.
func mustJSON(t *testing.T, body []byte, v any) {
	t.Helper()
	if err := json.Unmarshal(body, v); err != nil {
		t.Fatalf("decode JSON: %v (body=%s)", err, string(body))
	}
}
