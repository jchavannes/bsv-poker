# `relay-go` — Invariants

Run:
```
cd apps/relay-go && go test ./...
go test ./relay -run=^$ -fuzz=FuzzCapabilityVerify -fuzztime=60s
```

## Capability tokens — `relay/capability_test.go`

| ID | Claim | Proof (test) |
|---|---|---|
| INV-CAP-1 | Mint then verify succeeds for the right table + scope. | `TestCapabilityMintAndVerify` |
| INV-CAP-2 | A missing token is rejected (`ErrNoCapability`). | `TestCapabilityRejectsMissing` |
| INV-CAP-3 | A token for table A is rejected on table B. | `TestCapabilityRejectsWrongTable` |
| INV-CAP-4 | A `sub` token cannot publish (no scope escalation). | `TestCapabilityRejectsScopeEscalation` |
| INV-CAP-5 | An expired token is rejected. | `TestCapabilityRejectsExpired` |
| INV-CAP-6 | A tampered token (payload or MAC) is rejected. | `TestCapabilityRejectsTampering` |
| INV-CAP-7 | A token signed under a different secret is rejected (rotation revokes). | `TestCapabilityRejectsForeignSecret` |
| INV-CAP-8 | Admission hashing matches the correct secret and rejects a wrong one (constant-time). | `TestAdmissionGating` |

## HTTP fail-closed enforcement — `relay/capability_test.go`

| ID | Claim | Proof (test) |
|---|---|---|
| INV-CAP-9 | Publish without a token → 401; with a forged token → 403. | `TestPublishRequiresCapability` |
| INV-CAP-10 | Subscribe without a token → 401. | `TestSubscribeRequiresCapability` |
| INV-CAP-11 | Publish with a valid token → 200. | `TestPublishWithValidCapabilitySucceeds` |
| INV-CAP-12 | An open table mints freely but the token is still required to use the channel. | `TestMintCapabilityOpenTable` |
| INV-CAP-13 | A gated table refuses minting without the admission secret (403) and accepts it with. | `TestMintCapabilityGatedTable` |
| INV-CAP-F1 | `verify` never panics on arbitrary token bytes. | `FuzzCapabilityVerify` (CI, ~1M execs) |

## Transport (rate limit, oversize, fan-out) — `relay/relay_test.go`, `ratelimit_test.go`

| ID | Claim | Proof |
|---|---|---|
| INV-CAP-14 | One publish reaches every subscriber (Tier-B fan-out); the buffer is copied per delivery. | `TestFanoutDeliversToAllSubscribers`, `TestPublishCopiesBuffer` |
| INV-CAP-15 | Per-table publish quota bounds floods. | `ratelimit_test.go` |

## To extend

A new endpoint/auth rule adds the fail-closed HTTP test (missing/forged/expired/wrong-scope → 4xx) and,
for any token/bytes parsing, keeps `FuzzCapabilityVerify` green.
