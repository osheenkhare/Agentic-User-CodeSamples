# Teams SDK capabilities demo script

## Opening

> This software delivery assistant demonstrates Teams-native AI streaming,
> conversation memory boundaries, Adaptive Cards, GitHub Actions analysis,
> proactive notifications, reactions, citations, and feedback.

## 1. Streaming and natural conversation memory

In a 1:1 chat, send:

> Create a safe rollout plan for a new API version.

Highlight the processing reaction, informative update, streamed response,
completion reaction, AI label, feedback controls, and suggested actions.

Then send:

> My project codename is Bluebird. Keep that in mind.

Ask:

> What is my project codename?

Explain that no memory command was required. The bounded history belongs to the
active 1:1 conversation.

## 2. Prove that private context is not shared

In a channel, ask:

> @Software Delivery Assistant What is my project codename?

The agent does not receive the private 1:1 history. Explain that the boundary is
enforced when the application selects conversation history, before the model is
called.

In one channel thread, send:

> @Software Delivery Assistant Remember that the shared release window is Friday.

Ask for the release window in that thread, then ask in another thread. The
second thread has a different memory key.

## 3. Show optional memory diagnostics

In 1:1, send:

> `/memory-demo`

Select **Save Bluebird in this scope**, then send `/memory`. Highlight:

- The **Private 1:1** scope.
- The opaque scope ID.
- The item count and expiration.
- The fact that no other scope is queried or enumerated.

Send `/forget`, approve **Clear this scope**, and show that only the active scope
is cleared.

## 4. Analyze GitHub Actions

Send `/github`, select the repository and run filters, then choose **Analyze**.
Continue chatting while the work runs. Show the proactive completion message,
then send:

> Show latest GitHub Actions analysis.

Highlight real workflow evidence, links, citations, feedback, and suggested
remediation.

## 5. Turn reactions into actions

Add supported reactions to an agent response:

- Like records positive feedback.
- Checkmark creates an implementation checklist.
- Pushpin creates and saves an action item.
- Exclamation creates remediation and rollback guidance.

Show the immediate eyes acknowledgement, then send `/actions` to list saved
items.

## 6. Reset the active scope

Send `/reset`, then ask what was discussed. Explain that the active
conversation history, explicit scoped facts, and saved action items were
cleared without changing another chat or thread.

## Features shipped

| Feature | Demo evidence |
| --- | --- |
| Personal-chat AI streaming | Informative update and streamed rollout plan |
| Natural bounded history | Bluebird recalled in the same 1:1 |
| Private memory isolation | Bluebird unavailable in a channel |
| Group-chat isolation | Each group conversation has a separate scope |
| Channel-thread isolation | Shared release window unavailable in another thread |
| Explicit scoped memory | `Remember that...` and **Save Bluebird** |
| Opaque scope diagnostics | `/memory-demo` and `/memory` |
| TTL and item limits | Expiration and count shown on the status card |
| User-bound card actions | Single-use memory action scope |
| Scope-specific forgetting | `/forget` confirmation |
| Session reset | `/reset` clears only the current conversation state |
| GitHub Actions analysis | Structured `/github` card |
| Background processing | Chat remains available during analysis |
| Proactive completion | Completion arrives without another prompt |
| Citations and links | Workflow report links to inspected runs |
| Reaction workflows | Like, checkmark, pushpin, and exclamation |
| Saved action items | Pushpin plus `/actions` |
| AI metadata and feedback | AI label and response controls |
| Suggested actions | Follow-up prompt buttons |
| Channel replies | Responses stay attached to the channel thread |

## Closing

> The sample keeps the interaction conversational while enforcing memory
> boundaries in application code. Private 1:1 context is never automatically
> promoted into a shared Teams conversation.
