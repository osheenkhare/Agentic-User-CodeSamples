# C# progressive updates

This lab contains two small, independent Microsoft Teams SDK patterns:

- **Edit-based streaming:** periodically edits one message with the response
  accumulated so far. This provides a streaming-like experience in group chats
  and channels, where native Teams streaming is not supported.
- **Work-plan card:** updates one Adaptive Card as user-visible work steps move
  from pending to in progress to complete.

Both examples use generated demo content and require no model provider. See
[USER_GUIDE.md](./USER_GUIDE.md) to reuse either pattern in another agent.

## Prerequisites

- Complete the repository [Agentic User Setup](../Agentic-User-Setup.md).
- Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
- Configure a public HTTPS endpoint that forwards to local port `3978`.

## Configuration

Copy `appsettings.example.json` to `appsettings.json`, then replace the three
Azure AD placeholders with the values from the agent blueprint.

Do not commit the populated `appsettings.json`; it is ignored by the repository.

## Run locally

From this directory:

```powershell
dotnet restore
dotnet run
```

The app exposes:

- Teams endpoint: `https://<public-host>/api/messages`
- Health endpoint: `http://localhost:3978/health`

Configure the agent blueprint notification URL with the public Teams endpoint.

## Try the samples

Send these commands to the agent:

| Command | Result |
| --- | --- |
| `/edit-stream` | Sends one placeholder and progressively edits it with accumulated text. |
| `/work-plan` | Sends one work-plan card and updates each step until the card completes. |

Both commands work in personal chats, group chats, and channel threads. The
edit-based approach is primarily useful in shared conversations because native
streaming is already available in personal chats.
