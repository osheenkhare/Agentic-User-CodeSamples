# PowerShell Agent Identity Blueprint permissions

This lab uses
[`Configure-AgentIdentityBlueprint.ps1`](./Configure-AgentIdentityBlueprint.ps1)
to configure resource permissions for an existing Agent Identity Blueprint.

The script can:

- Ensure each configured resource service principal exists.
- Add delegated scopes and application roles to the blueprint's required
  resource access.
- Configure permissions inherited by agent identities created from the
  blueprint.
- Create or extend tenant-wide delegated permission grants when
  `GrantConsent` is enabled.
- Print Microsoft Graph Explorer requests for operations that require manual
  administrator follow-up.

The script is idempotent: rerunning it reuses existing objects and merges
permissions rather than replacing the complete permission list.

## Before you begin

Complete the repository [Agentic User Setup](../Agentic-User-Setup.md) through
creation of the Agent Identity Blueprint. Record its application (client) ID.

You also need:

- Windows PowerShell 5.1 or PowerShell 7 or later.
- The
  [Microsoft Graph PowerShell SDK](https://learn.microsoft.com/powershell/microsoftgraph/installation).
- A Microsoft Entra administrator account with roles appropriate for the
  requested operations. Depending on tenant policy, these can include Agent ID
  Administrator, Application Administrator or Cloud Application Administrator,
  and Privileged Role Administrator.
- Approval to grant the permissions declared in the script's
  `$ResourceConfiguration`.

The script requests these delegated Microsoft Graph scopes when it connects:

- `Application.ReadWrite.All`
- `AgentIdentityBlueprint.ReadWrite.All`
- `DelegatedPermissionGrant.ReadWrite.All`
- `Directory.ReadWrite.All`

## 1. Install the Microsoft Graph PowerShell SDK

Open PowerShell and run:

```powershell
Install-Module Microsoft.Graph -Scope CurrentUser
```

If your organization restricts PowerShell Gallery access, follow its approved
module installation process instead.

## 2. Review the configured permissions

Review the `$ResourceConfiguration` section in
`Configure-AgentIdentityBlueprint.ps1` before running it.

The included configuration declares selected Microsoft Graph delegated scopes
and the SMBA `AgentData.ReadWrite` scope. If the agent requires a different
permission set, leave the script unchanged and supply an approved external JSON
configuration with `-ConfigPath`, as shown below.

To print the effective configuration without signing in or contacting
Microsoft Graph:

```powershell
.\Configure-AgentIdentityBlueprint.ps1 `
  -BlueprintAppId <blueprint-app-id> `
  -ShowConfig
```

## 3. Preview the Microsoft Graph operations

Run preview mode before applying changes:

```powershell
.\Configure-AgentIdentityBlueprint.ps1 `
  -BlueprintAppId <blueprint-app-id> `
  -WhatIfOnly
```

To explicitly select the tenant:

```powershell
.\Configure-AgentIdentityBlueprint.ps1 `
  -BlueprintAppId <blueprint-app-id> `
  -TenantId <tenant-id> `
  -WhatIfOnly
```

The script opens an interactive Microsoft Graph sign-in if the current session
does not already have the required scopes. Review every proposed operation and
resolve any unexpected permission or tenant selection before continuing.

## 4. Apply the configuration

After an administrator approves the preview, rerun without `-WhatIfOnly`:

```powershell
.\Configure-AgentIdentityBlueprint.ps1 `
  -BlueprintAppId <blueprint-app-id> `
  -TenantId <tenant-id>
```

Add `-Verbose` to include detailed request logging:

```powershell
.\Configure-AgentIdentityBlueprint.ps1 `
  -BlueprintAppId <blueprint-app-id> `
  -TenantId <tenant-id> `
  -Verbose
```

## Optional parameters

| Parameter | Purpose |
| --- | --- |
| `-ConfigPath <path>` | Replaces the built-in resource configuration with an external JSON configuration. |
| `-ShowConfig` | Prints the effective configuration and exits without contacting Microsoft Graph. |
| `-WhatIfOnly` | Prints proposed requests without changing the tenant. |
| `-GraphApiVersion v1.0` | Uses the production Microsoft Graph endpoint. This is the default. |
| `-AllowUseOfBetaApis` | Allows fallback to Microsoft Graph beta for blueprint APIs unavailable on `v1.0`. |
| `-GraphApiVersion beta -AllowUseOfBetaApis` | Runs against beta explicitly. Use only after reviewing preview API requirements. |

Beta API calls are blocked by default. If a required blueprint API is not
available on `v1.0`, the script reports a manual Graph Explorer operation
instead of silently using beta.

## External configuration

Use `-ConfigPath` to supply a JSON array instead of editing the built-in
configuration:

```json
[
  {
    "Name": "Microsoft Graph",
    "AppId": "00000003-0000-0000-c000-000000000000",
    "DelegatedScopes": [
      "User.Read"
    ],
    "AppRoles": [],
    "InheritScopes": "allAllowed",
    "InheritRoles": "none",
    "GrantConsent": true
  }
]
```

Run it first with `-ShowConfig`, then with `-WhatIfOnly`:

```powershell
.\Configure-AgentIdentityBlueprint.ps1 `
  -BlueprintAppId <blueprint-app-id> `
  -ConfigPath .\resources.json `
  -ShowConfig
```

## Results and manual follow-up

The script exits with:

- `0` when preview or apply completes successfully.
- `1` when one or more operations failed or require manual follow-up.

When an operation cannot be completed, the summary prints its HTTP method,
Microsoft Graph URL, JSON payload, and reason. Give that output to an
authorized administrator to review and, if approved, execute in
[Microsoft Graph Explorer](https://developer.microsoft.com/graph/graph-explorer).

Do not paste access tokens, client secrets, or other credentials into the
script or external configuration file.
