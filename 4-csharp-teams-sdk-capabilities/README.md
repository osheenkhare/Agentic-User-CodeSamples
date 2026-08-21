# C# Teams SDK capabilities

This sample is a software delivery assistant that demonstrates a complete Microsoft Teams SDK journey with Azure OpenAI and GitHub Actions.

## Capability matrix

| Capability | Demonstration |
| --- | --- |
| Azure OpenAI | Streams model output in personal chats |
| Conversation memory | Keeps the latest 12 user/assistant messages within the active conversation boundary |
| Natural memory isolation | Keeps ordinary 1:1 history out of group chats, channels, and other channel threads |
| Explicit scoped memory | Separates private, group-chat, and channel-thread facts with bounded retention |
| Memory diagnostics | `/memory-demo` and `/memory` show the active scope without enumerating another scope |
| Scoped forgetting | `/forget` clears only the active scope after confirmation |
| Session controls | `/reset` clears current-scope memory and saved action items; `/actions` lists saved items |
| Reactions | Adds processing and completion reactions and handles user reaction triggers |
| AI response UX | Adds AI-generated metadata, feedback controls, citations, and suggested prompts |
| Adaptive Cards | `/github` opens a GitHub Actions analysis form |
| Background work | Queues analysis and proactively notifies the originating conversation |
| GitHub reports | Produces a cited report from workflow runs and failed steps |
| Channel behavior | Replies in channel threads; sends flat messages in personal and group chats |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Microsoft Teams agent blueprint configured for API-based notifications
- An Azure OpenAI resource with a chat model deployment
- A GitHub token with read access to repository metadata and Actions
- A public HTTPS development endpoint that forwards to local port `3978`

## Configuration

Open `appsettings.json` and replace only the blank or placeholder values:

```json
{
  "AzureAd": {
    "ClientId": "<agent-blueprint-application-id>",
    "TenantId": "<tenant-id>",
    "ClientCredentials": [
      {
        "SourceType": "ClientSecret",
        "ClientSecret": "<client-secret>"
      }
    ]
  },
  "AzureOpenAI": {
    "Endpoint": "https://<resource-name>.openai.azure.com/",
    "ApiKey": "<api-key>",
    "Deployment": "<chat-model-deployment>"
  },
  "GitHub": {
    "Token": "<github-token>",
    "DefaultRepository": "microsoft/teams.net"
  }
}
```

ASP.NET Core configuration overrides remain available. For example, user secrets or environment variables such as `AzureOpenAI__ApiKey` can replace local JSON values without changing code.

The optional `Memory` section controls the explicit demo store:

```json
{
  "Memory": {
    "LifetimeMinutes": 60,
    "MaximumItemsPerScope": 20
  }
}
```

Do not commit populated credentials.

## Run locally

From this directory:

```bash
dotnet restore
dotnet run
```

The app listens on `http://localhost:3978` by default:

- Teams notification endpoint: `https://<public-host>/api/messages`
- Health endpoint: `http://localhost:3978/health`

Configure the agent blueprint notification URL with the public HTTPS `/api/messages` endpoint, then start a conversation with the agent in Teams.

## End-to-end demo

1. In a personal chat, ask: `Create a safe rollout plan for a new API version.` Observe the processing reaction, informative update, streamed response, completion reaction, feedback controls, and suggested prompts.
2. In the same 1:1, say `My project codename is Bluebird. Keep that in mind.`, then ask for the codename.
3. Ask for the codename in a channel. The private 1:1 history is not supplied to that request.
4. Send `/memory-demo` for optional scope diagnostics. Save the Bluebird demo fact, inspect `/memory`, and use `/forget` to clear only that scope.
5. In one channel thread, remember a shared release window. Ask in another thread to demonstrate thread isolation.
6. Send `/reset`, then ask what was discussed to confirm the current memory boundary was cleared.
7. Send `/github`. Keep `microsoft/teams.net` or enter another accessible `owner/repository`, select filters, and choose **Analyze**.
8. Continue chatting while analysis runs. The background worker proactively posts a completion notification.
9. Send `show latest GitHub Actions analysis` to receive a cited report with links to the inspected workflow runs.
10. React to an agent response with 👍, ✅, 📌, or ❗ and observe the immediate 👀 acknowledgement and follow-up behavior.
11. Send `/actions` to review items saved by 📌.

For a presenter-ready walkthrough, see [DEMO-SCRIPT.md](./DEMO-SCRIPT.md).

## Reaction mapping

| Reaction | Action |
| --- | --- |
| 👍 Like | Records positive feedback |
| ✅ Checkmark | Generates an implementation checklist with validation and rollback checks |
| 📌 Pushpin | Generates and saves the next action item for the conversation |
| ❗ Exclamation | Generates remediation, rollback, validation, and escalation guidance |
| 👀 Eyes | Acknowledges that a supported reaction trigger is being processed |

The sample accepts both Teams SDK reaction aliases and raw catalog reaction IDs.

## Architecture and state

`Program.cs` registers Teams event handlers for messages, reactions, Adaptive Card actions, and feedback submissions. `ConversationAgent` serializes model turns per conversation and retains a bounded history. `ConversationScopeResolver` derives non-overlapping keys for personal, group-chat, and channel-thread memory before model invocation. `ScopedMemoryStore` applies TTL and item limits. `MemoryActionScopeStore` binds Adaptive Card actions to the original user, conversation, and resolved scope because invoke activities may not preserve the original conversation type. `GitHubActionsService` reads workflow runs and jobs, caches the latest completed report per conversation, and supplies citation data. `BackgroundTaskQueue` runs card-triggered analysis after the invoke request completes. `ActionItemStore` keeps reaction-generated items per conversation.

Personal chats have an independent conversation memory. Participants in a group chat share that group's conversation memory. Each Teams channel thread has its own conversation identifier and therefore its own memory.

All state is in process memory. Restarting the app clears conversations, cached reports, queued work, and action items. Multiple app instances do not share state.

Visible threaded replies are channel-only. Teams personal and group chats use a flat message timeline, so responses there are sent as normal chat messages rather than visible threads.

## Security and production notes

- Store credentials in user secrets, a managed secret store, or workload identity instead of populated JSON files.
- Prefer managed or federated identity for Azure services where supported.
- Scope the GitHub token to read-only access for only the repositories required.
- Validate repository authorization separately if users must not inspect every repository accessible to the configured token.
- Replace in-memory history, reports, action items, and work queues with durable, tenant-aware services before scaling out.
- Keep the same scope-key inputs and user/conversation binding if memory moves to durable storage.
- Do not allow user input to select arbitrary memory keys or enumerate other scopes.
- Add retry policies, telemetry, rate-limit handling, and operational alerting appropriate to the deployment.
- Treat model output and external API data as untrusted content; keep human approval around destructive actions.
