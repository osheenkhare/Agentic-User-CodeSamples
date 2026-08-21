# Software Delivery Assistant: Developer Companion Demo

## Demo story

A developer is working on an API rollout. The assistant helps the developer
retain context, monitor CI, investigate failures, organize follow-up work, and
collaborate in a channel without moving private 1:1 context into shared spaces.

Recommended length: 5-7 minutes.

## Journey at a glance

| Stage | Developer activity | Assistant role | Capability shown |
| --- | --- | --- | --- |
| 1. Start work | Orient around an API rollout | Help frame the task and retain private context | Streaming AI and bounded history |
| 2. Check CI | Inspect current workflow health | Collect filters and analyze GitHub Actions | Adaptive Card and GitHub evidence |
| 3. Keep moving | Continue working while CI analysis runs | Notify when analysis completes | Background work and proactive messaging |
| 4. Investigate | Understand a failed run | Provide cited findings and next actions | Citations and reaction workflows |
| 5. Collaborate | Bring the task into a channel | Reply in-thread without exposing private context | Channel replies and memory isolation |
| 6. Close the loop | Review and clear follow-ups | Preserve developer control over state | `/actions`, `/forget`, and `/reset` |

## 1. Start work in 1:1

Send:

> Create a safe rollout plan for a new API version.

Highlight:

- Processing and completion reactions.
- The informative update and streamed response.
- AI metadata, feedback controls, and suggested actions.

Then send:

> My project codename is Bluebird. Keep that in mind.

Ask:

> What is my project codename?

Explain that ordinary conversation history is retained within the active 1:1;
the developer did not need a special memory command.

## 2. Check CI without leaving Teams

Send `/github`, choose the repository and run filters, then select **Analyze**.

Continue the conversation while analysis runs. When the proactive completion
arrives, send:

> Show the latest GitHub Actions analysis.

Highlight the real workflow evidence, failed steps, links, citations, feedback,
and suggested remediation.

## 3. Turn findings into follow-up work

Add supported reactions to the report:

- Exclamation creates remediation and rollback guidance.
- Checkmark creates an implementation checklist.
- Pushpin creates and saves the next action item.
- Like records positive feedback.

Show the immediate eyes acknowledgement, then send `/actions`.

Explain that the developer can turn analysis into the next useful artifact
without repeating the original task.

## 4. Prove private context stays private

In a channel, ask:

> @Software Delivery Assistant What is my project codename?

The assistant does not receive the private 1:1 history.

In one channel thread, send:

> @Software Delivery Assistant Remember that the shared release window is Friday.

Ask for the window in that thread, then ask in another thread. The second
thread has a different memory boundary.

Explain that memory scope is resolved in application code before the model is
called.

## 5. Inspect and clear one scope

In 1:1, send `/memory-demo`.

Select **Save Bluebird in this scope**, then send `/memory`. Highlight:

- The **Private 1:1** label.
- The opaque scope ID.
- Item count and expiration.
- The fact that no other scope is queried or enumerated.

Send `/forget` and approve **Clear this scope**. Only the active scope changes.

Finally send `/reset` to clear current conversation history, explicit memory,
and saved action items.

## Features shipped

| Feature | Developer value |
| --- | --- |
| Personal-chat AI streaming | Delivers useful guidance without waiting for a complete response |
| Natural bounded history | Supports follow-up work without repeating context |
| Private 1:1 isolation | Keeps personal working context out of shared conversations |
| Group-chat isolation | Gives each group conversation an independent scope |
| Channel-thread isolation | Keeps parallel channel workstreams separate |
| Explicit scoped memory | Provides a deterministic memory demonstration |
| Scope diagnostics | Shows scope type, opaque ID, count, and expiration |
| TTL and item limits | Bounds in-process memory retention |
| User-bound card actions | Prevents another user or conversation from reusing a memory action |
| Scope-specific forgetting | Clears one memory boundary without changing another |
| GitHub Actions analysis | Brings real CI evidence into Teams |
| Background processing | Lets the developer continue working during analysis |
| Proactive completion | Notifies the developer when results are ready |
| Citations and source links | Keeps findings traceable to workflow runs |
| Reaction workflows | Produces remediation, checklists, feedback, and saved actions |
| Channel-thread replies | Keeps collaboration attached to the correct discussion |
| AI metadata and feedback | Adds native Teams response controls |
| Suggested actions | Guides the developer toward useful next steps |

## Capability coverage checklist

- [ ] Personal-chat AI streaming and informative update.
- [ ] Processing and completion reactions.
- [ ] Natural conversation memory and contextual follow-up.
- [ ] AI-generated metadata.
- [ ] Rendered Teams feedback controls.
- [ ] Suggested actions.
- [ ] GitHub Actions Adaptive Card.
- [ ] Background processing and proactive completion.
- [ ] Cited GitHub Actions report with source links.
- [ ] Exclamation remediation guidance.
- [ ] Checkmark implementation checklist.
- [ ] Pushpin saved action and `/actions`.
- [ ] Like reaction feedback.
- [ ] `/reset` current-session reset.
- [ ] Channel-thread replies.
- [ ] Natural private 1:1 memory isolation.
- [ ] Group-chat and channel-thread memory boundaries.
- [ ] `/memory-demo`, `/memory`, `/forget`, TTL, and opaque scope IDs.

## Closing

> The assistant is a lightweight developer companion: it keeps context where it
> belongs, surfaces CI information when it becomes actionable, and helps turn
> findings into the next step without replacing the source systems or the
> developer's judgment.
