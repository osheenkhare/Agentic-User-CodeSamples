# Agentic user samples

These C# samples demonstrate agent identities with agent's user accounts in Microsoft Teams. They range from basic message handling to Microsoft Graph, Azure OpenAI, MCP tools, and advanced Microsoft Teams SDK capabilities.

## Get started

1. Complete the [Agent's User Account Setup](./Agentic-User-Setup.md) to create an agent identity blueprint and agent identity, provision the agent's user account, and configure the messaging endpoint.
2. Choose and configure one of the samples below. Start with [C# Hello World](./1-csharp-hello-world/Readme.md) for the simplest end-to-end example.

The samples require the .NET 8 SDK. Individual samples document their additional service and configuration requirements.

## Samples

| Sample | Demonstrates | Additional services |
| --- | --- | --- |
| [1 - C# Hello World](./1-csharp-hello-world/Readme.md) | Basic Teams message handling and streaming | None |
| [2 - C# Microsoft Graph API](./2-csharp-graph-api/Readme.md) | Microsoft Graph application access and streamed responses | Microsoft Graph app registration |
| [3 - C# Azure OpenAI](./3-csharp-azure-openai/Readme.md) | Model streaming and Microsoft Learn MCP tool invocation | Azure OpenAI |
| [4 - C# Teams SDK Capabilities](./4-csharp-teams-sdk-capabilities/README.md) | Reactions, Adaptive Cards, background work, notifications, and conversation memory | Azure OpenAI and GitHub |

## Demo video

The recording shows an agent's user account receiving and responding to messages in Microsoft Teams.

![Demo](./diagrams/Demo.gif?raw=true)

## High-level architecture

All samples use the same Teams-to-agent-service message flow. Integrations such as Microsoft Graph, Azure OpenAI, MCP servers, and line-of-business services are sample-specific.

![High-level design](./diagrams/HLD.png)
