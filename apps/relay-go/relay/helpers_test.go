package relay

import (
	"io"
	"strings"
)

// jsonBody wraps a literal JSON string as a request body reader.
func jsonBody(s string) io.Reader { return strings.NewReader(s) }
