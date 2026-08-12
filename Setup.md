# Agentic User Setup

Follow these steps to create an agent blueprint and identity, provision an agentic user, and make the user available in Microsoft Teams.

## Prerequisites

- Access to the [Microsoft Entra admin center](https://entra.microsoft.com/)
- Access to the [Microsoft Teams Developer Portal](https://dev.teams.microsoft.com/)
- Access to [Microsoft Graph Explorer](https://developer.microsoft.com/graph/graph-explorer)
- Permissions to create agent identities, users, and OAuth permission grants
- An available Microsoft 365 E3 license

## 1. Create an Agent Blueprint

### Option A: Manually
1. Open [Agent Blueprints in the Microsoft Entra admin center](https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/AllAgents.MenuView/~/overview).
2. Create an agent blueprint.
4. Save the following values for later:
   - Agent `appId`
   - Agent `principalObjectId`
5. Create a client secret for authentication and store it securely.
6. Open the blueprint in the Teams Developer Portal, replacing `{appId}` with the saved agent application ID:

   ```text
   https://dev.teams.microsoft.com/tools/agent-blueprint/{appId}
   ```

7. Set the agent endpoint URL, this is the service on which the agent is hosted `{host}/api/messages`.
8. In the Agent service update the `clientId (appId)`, `clientSecret` and `tenantId`.

### Option B: Graph APIs

First Get the current User Id
```http
GET https://graph.microsoft.com/v1.0/me
```

Create Agent Blueprint
```http
POST https://graph.microsoft.com/v1.0/applications/microsoft.graph.agentIdentityBlueprint
Content-Type: application/json
OData-Version: 4.0

{
  "displayName": "My agent blueprint",
  "sponsors@odata.bind": [
    "https://graph.microsoft.com/v1.0/users/{user-id}"
  ]
}
```
Note: Client secret and setting callback url is still manual

## 2. Assign the SMBA Permission

In Microsoft Graph Explorer, submit the following request. Replace `<principalObjectId>` with the value saved in step 1; leave the other values unchanged.

```http
POST https://graph.microsoft.com/v1.0/oauth2PermissionGrants
Content-Type: application/json
```

```json
{
  "clientId": "<principalObjectId>",
  "consentType": "AllPrincipals",
  "id": "8jJE7cV0qUGWNscC4cKioagfINH8oytAkUfEcUyZSTA",
  "principalId": null,
  "resourceId": "d1201fa8-a3fc-402b-9147-c4714c994930",
  "scope": "AgentData.ReadWrite"
}
```

## 3. Create an Agent Identity

### Option A: Microsoft Entra Admin Center

1. Open [Agent Identities in the Microsoft Entra admin center](https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/AllAgents.MenuView/~/allAgentIds).
2. Create an agent identity based on the blueprint from step 1.
3. Save the agent identity object ID for the next step.

### Option B: Microsoft Graph

The caller needs the `AgentIdentity.Create.All` permission. Replace the placeholders with the agent identity name, the blueprint object ID from step 1, and the object ID of a sponsoring user or group.

```http
POST https://graph.microsoft.com/beta/servicePrincipals/microsoft.graph.agentIdentity
Content-Type: application/json
```

```json
{
  "displayName": "<agentIdentityName>",
  "agentIdentityBlueprintId": "<agentIdentityBlueprintId>",
  "sponsors@odata.bind": [
    "https://graph.microsoft.com/v1.0/users/<sponsorUserId>"
  ]
}
```

## 4. Create an Agentic User

In Microsoft Graph Explorer, submit the following request. Replace the example values as needed and set `identityParentId` to the agent identity object ID saved in step 3.

```http
POST https://graph.microsoft.com/beta/users/microsoft.graph.agentUser
Content-Type: application/json
```

```json
{
  "accountEnabled": true,
  "displayName": "Pheonix",
  "mailNickname": "Pheonix",
  "userPrincipalName": "Pheonix@dptest07.onmicrosoft.com",
  "identityParentId": "<agentIdentityObjectId>"
}
```

Save the `id` returned for the new agentic user.

## 5. Set the Usage Location

Replace `{agenticUserId}` with the agentic user ID saved in step 4, then submit the request:

```http
PATCH https://graph.microsoft.com/beta/users/microsoft.graph.agentUser/{agenticUserId}
Content-Type: application/json
```

```json
{
  "usageLocation": "US"
}
```

## 6. Assign a Microsoft 365 E3 License

Replace `{agenticUserId}` with the agentic user ID saved in step 4. The following payload uses the Microsoft 365 E3 SKU and should otherwise remain unchanged.

```http
POST https://graph.microsoft.com/v1.0/users/{agenticUserId}/assignLicense
Content-Type: application/json
```

```json
{
  "addLicenses": [
    {
      "skuId": "6fd2c87f-b296-42f0-b197-1e91e994b900",
      "disabledPlans": []
    }
  ],
  "removeLicenses": []
}
```

## 7. Verify the Agentic User in Teams

Open Microsoft Teams, locate the newly created agentic user, and send the user a message to verify that the setup works.

