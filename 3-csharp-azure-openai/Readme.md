# Azure OpenAI Sample

This sample demonstrates an agentic user in Microsoft Teams backed by Azure OpenAI. It streams model responses to Teams and uses the Microsoft Learn Model Context Protocol (MCP) server to discover and invoke tools. Informative updates, such as thinking and tool invocation status, are sent before the streamed response begins.

## Prerequisites

- Complete the [Agentic User Setup](../Agentic-User-Setup.md).
- [Create an Azure OpenAI resource and deploy a supported chat model](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/how-to/create-resource?pivots=web-portal).
- [Obtain the model deployment name, Azure OpenAI v1 endpoint, and API key](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/how-to/switching-endpoints).

## Azure OpenAI model deployment in Microsoft Foundry

- Sign in to the [Microsoft Foundry portal](https://ai.azure.com/) and open or create a Foundry project.
- Review the [supported Azure OpenAI models](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure?pivots=azure-openai) and confirm that the model is [available in your region](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability).
- Follow the [Foundry model deployment guide](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/deploy-foundry-models) to select a model, choose a deployment type, assign quota, and create the deployment.
- Record the **deployment name**. Azure OpenAI API calls use the deployment name, rather than the underlying model name, as the `model` value.
- After the deployment succeeds, open **Build > Models**, select the deployment, and copy its endpoint and API key. See the [Azure OpenAI endpoint and authentication guidance](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/how-to/switching-endpoints#authentication).
- Use those values in the `MODEL`, `AZURE_OPENAI_BASE_URL`, and `AZURE_OPENAI_API_KEY` settings below.

## Setup `appsettings.json`

Go to `appsettings.json`, leave the other values as-is, and configure the following Azure OpenAI settings:

```json
{
  "MODEL": "<Azure OpenAI model deployment name>",
  "AZURE_OPENAI_BASE_URL": "https://<resource-name>.openai.azure.com/openai/v1/",
  "AZURE_OPENAI_API_KEY": "<Azure OpenAI API key>"
}
```

Configure the agent blueprint identity settings:

```json
{
  "AzureAd": {
    "ClientId": "<agent blueprint app ID configured during agent blueprint setup>",
    "TenantId": "<tenant ID where the agent blueprint is configured>",
    "ClientCredentials": [
      {
        "SourceType": "ClientSecret",
        "ClientSecret": "<client secret created for the agent blueprint>"
      }
    ]
  }
}
```

> Do not commit API keys or client secrets to source control. Use user secrets, environment variables, or a secure secret store for non-demo environments.

## Running the sample locally

### Install .NET 10+

Make sure the .NET 10+ SDK is installed. You can check the installed SDKs by running:

```powershell
dotnet --list-sdks
```

If .NET is not installed, install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download).

### Run the server locally

```powershell
dotnet build; dotnet run --no-build
```

This runs the server locally at `http://localhost:3978`.

The application connects to the Microsoft Learn MCP server at `https://learn.microsoft.com/api/mcp`. Internet access is required when the application starts so that it can retrieve the available MCP tools.

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

In Microsoft Teams, search for the agentic user and start a chat. Ask a question about Microsoft products or documentation. The agent streams its response and can invoke tools from the Microsoft Learn MCP server when additional documentation is needed.
