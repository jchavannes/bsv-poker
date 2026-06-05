# `@bsv-poker/ui-core` — Security model

View-models and components. "No just-UI": the UI is a security boundary because it enforces
**human control** and must never become a covert state or value store.

## Security obligations

| Obligation | Why | Where |
|---|---|---|
| **A human selects every action** | No AI/bot/default may choose a gameplay or money action on a person's behalf (project hard rule). The UI is menu-driven; bots are test-only. | view-models / components |
| **No LOAD-BEARING state in web storage** | localStorage/sessionStorage is attacker-modifiable and not authoritative; truth is the engine-reconstructed transcript (REQ-UI-002/APP-042). | storage tests |
| **Explicit handlers, no implicit submit** | no `<form>`/`onSubmit` auto-submission of a money action (REQ-UI-003/APP-053). | interaction-rules tests |
| **Accessibility = unambiguous actions** | colour-independent rank+suit labels so an action is never misread (REQ-APP-054). | accessibility tests |
| **Real-value warnings surfaced** | the play-money / regtest / mainnet banner from the network gate is shown, never hidden. | view-models |

## Trust boundary

- **Trusted:** the human operator's explicit selections.
- **Untrusted:** any value read back from web storage or the DOM — re-derived/validated, never trusted
  as authoritative state.
- **Side effects:** rendering + dispatching the human's chosen action to `app-services`.

## What breaks if violated

A default/auto action would take a money decision the human did not make; load-bearing localStorage
would let an attacker edit "truth"; an implicit form submit could fire an unintended wager.
