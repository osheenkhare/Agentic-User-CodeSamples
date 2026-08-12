# Teams Streaming App for BNY

Demo for showcasing agentic users for BNY's user case.

## Demo
- Shows an Agentic user created via Agent Identity
- Simulated tool calling (Gitlab, Jira) via MCP servers
- Supports Status updates and Chain of thought resoning updates while processing requests
- Supports Streaming Responses 
- Built with Teams SDK

![Demo](./Demo.gif?raw=true)

## High level architecture 

![High-level design](./diagrams/HDL-sdkPassthru.png)


## Running locally


1. Run the app:

   chsarp
   ```powershell
   dotnet build; dotnet run --no-build;
   ```

2. In another terminal, launch Microsoft 365 Agents Playground:

   ```powershell
   npx @microsoft/m365agentsplayground -e http://localhost:3978/api/messages -c emulator
   ```

The app listens on `http://localhost:3978` and exposes `POST /api/messages`.
