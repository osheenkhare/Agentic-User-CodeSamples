# Microsoft Graph API Sample

This sample demonstrates an agentic user in Microsoft Teams that calls Microsoft Graph and streams a response. When a message is received, the application uses the sender's Microsoft Entra object ID to retrieve their display name and email address from Microsoft Graph. It sends an informative status update and then streams the result back to Teams word by word.

## Prerequisites

- Complete the [Agentic User Setup](../Agentic-User-Setup.md).
- Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download).

> This sample uses the OAuth 2.0 client credentials flow and the Microsoft Graph `.default` scope. It therefore requires an application permission, not a delegated permission.

## Setup `appsettings.json`

Go to `appsettings.json`, configure the agent blueprint identity:

```json
{
  "AzureAd": {
    "ClientId": "<agent blueprint app ID configured during agent blueprint setup>",
    "TenantId": "<tenant ID where the agent blueprint is configured>",
    "ClientCredentials": [
      {
        "SourceType": "ClientSecret",
        "ClientSecret": "<agent blueprint client secret>"
      }
    ]
  }
}
```

### Configure the Microsoft Graph app registration:

You can use your own Microsoft Entra app registration for Microsoft Graph access. This app registration is used to call Microsoft Graph from the agentic user. You can add the permissions required by the business logic of your agentic user. For this sample, the app registration requires the `User.Read.All` application permission.

Steps to create a Microsoft Graph app registration
- For the Graph app registration, goto [App registrations](https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade/quickStartType~/null/sourceType/Microsoft_AAD_IAM) > Select new registration > Enter a name > Leave other values as-is > Select Register. It will create an app registration and take you to the Overview page.
  - Copy the Application (client) id 
  - Copy the Directory (tenant) id
- Navigate to **Certificates & secrets** > **New client secret** > Enter a description > Select Add. Copy the `value` of the client secret and store it securely. You will not be able to retrieve it again.
- Navigate to **API permissions** > **Add a permission** > **Microsoft Graph** > **Application permissions** > Search for `User.Read.All` > Select it > Select Add permissions. Then select **Grant admin consent for <tenant>**.

Update the `Graph` section in `appsettings.json` with the values from your Microsoft Graph app registration:

```json
{
  "Graph": {
    "ClientId": "<Graph app registration application (client) ID>",
    "TenantId": "<Microsoft Entra tenant ID>",
    "ClientSecret": "<Graph app registration client secret>"
  }
}
```

## Running the sample locally

### Verify the .NET SDK

Check that the .NET 10+ SDK is installed:

```powershell
dotnet --list-sdks
```

### Run the server locally

From this sample's directory, run:

```powershell
dotnet build; dotnet run --no-build
```

This runs the server locally at `http://localhost:3978`.

### Expose the server to the internet

For testing, expose the server to the internet so that Microsoft Teams can communicate with it. You can use tunneling software such as ngrok. This setup is for testing only and should not be used in production.

- Install [ngrok](https://ngrok.com/) and create a free account.
- Add your ngrok authentication token: `ngrok config add-authtoken <token>`.
- Run `ngrok http 3978`. It returns an HTTPS endpoint such as `https://domain.ngrok-free.dev`.

![ngrok](../diagrams/ngrok.png)

### Configure the ngrok endpoint

In the Microsoft 365 Developer Portal, open:

`https://dev.teams.microsoft.com/tools/agent-blueprint/<id>`

Replace `<id>` with the agent blueprint app ID created during setup.

Go to **Configuration > Notification Configuration** and set:

- **Agent Type**: `API Based`
- **Notification Url**: `https://domain.ngrok-free.dev/api/messages`

![Agent blueprint configuration](../diagrams/devPortalAb.png)

## Testing the sample

1. Open Microsoft Teams and search for the agentic user. You might need to search using its full email address the first time.
2. Start a chat and send any message.
3. Confirm that Teams first displays the `Fetching user details from Graph Api` status update.
4. Confirm that the response contains the sender's display name and email address retrieved from Microsoft Graph.

The word-by-word response includes a deliberate 0.1-second delay to demonstrate streaming. Remove the delay in `GraphAgentOrchestrator` before adapting the sample for production use.

## Troubleshooting

- **Missing configuration value**: Verify that all `AzureAd` and `Graph` settings are populated.
- **401 Unauthorized**: Verify the Graph tenant ID, client ID, and client secret. Ensure the secret value—not its secret ID—was configured.
- **403 Forbidden**: Verify that the Graph app has the `User.Read.All` application permission and that an administrator granted consent.
- **User not found**: Confirm that the Teams sender belongs to the configured tenant and that their Microsoft Entra object ID is available on the incoming activity.

For more information, see the [Microsoft Graph .NET SDK documentation](https://learn.microsoft.com/en-us/graph/sdks/sdk-installation) and the [Get user API documentation](https://learn.microsoft.com/en-us/graph/api/user-get).
