#Requires -Version 5.1
<#
===============================================================================================
 Configure-AgentIdentityBlueprint.ps1
===============================================================================================

 WHAT THIS SCRIPT DOES
 ---------------------
 For each resource API listed in the CONFIGURATION section below, this script:

   1. Ensures the resource's service principal exists in your tenant (creates it if missing).
   2. Adds the requested permissions to the Agent Identity Blueprint's
      "required resource access" (the permissions the blueprint declares it needs).
   3. Adds "inheritable permissions" on the blueprint, so agent identities created from
      the blueprint automatically inherit those permissions.
   4. Ensures the Agent Identity Blueprint's own service principal exists (creates it if missing).
   5. Creates / extends the OAuth2 delegated permission grant (admin consent, AllPrincipals)
      between the blueprint service principal (client) and the resource service principal.

 The script is IDEMPOTENT - it is safe to run more than once. Existing objects are reused
 and permission lists are merged, never overwritten.

 If any create or update call fails (for example, insufficient privileges), the script prints
 the exact HTTP method, URL and JSON payload so an administrator can complete that single
 operation manually in Microsoft Graph Explorer (https://aka.ms/ge). The script then continues.

 PREREQUISITES
 -------------
   * PowerShell 5.1+ or PowerShell 7+
   * Microsoft Graph PowerShell SDK:
         Install-Module Microsoft.Graph -Scope CurrentUser
   * A signed-in administrator holding an appropriate Microsoft Entra role, for example:
         - Agent ID Administrator   (for the blueprint / inheritable permission operations)
         - Application Administrator or Cloud Application Administrator
         - Privileged Role Administrator (required to grant admin consent)
   * Delegated Graph scopes (the script requests these on connect):
         Application.ReadWrite.All, AgentIdentityBlueprint.ReadWrite.All,
         DelegatedPermissionGrant.ReadWrite.All, Directory.ReadWrite.All

 HOW TO RUN
 ----------
   # 1. Preview everything without changing anything (RECOMMENDED FIRST RUN):
   .\Configure-AgentIdentityBlueprint.ps1 -BlueprintAppId <blueprint-app-id> -WhatIfOnly

   # 2. Apply:
   .\Configure-AgentIdentityBlueprint.ps1 -BlueprintAppId <blueprint-app-id>

   # 3. Apply against a specific tenant, with detailed request logging:
   .\Configure-AgentIdentityBlueprint.ps1 -BlueprintAppId <blueprint-app-id> `
        -TenantId <tenant-id> -Verbose

   # 4. Use an external JSON config instead of the built-in block:
   .\Configure-AgentIdentityBlueprint.ps1 -BlueprintAppId <blueprint-app-id> `
        -ConfigPath .\resources.json

   # 5. Allow the automatic /beta retry for blueprint APIs not yet on v1.0:
   .\Configure-AgentIdentityBlueprint.ps1 -BlueprintAppId <blueprint-app-id> -AllowUseOfBetaApis

   # 6. Run entirely against beta (requires the flag above):
   .\Configure-AgentIdentityBlueprint.ps1 -BlueprintAppId <blueprint-app-id> `
        -GraphApiVersion beta -AllowUseOfBetaApis

 EXIT CODES
 ----------
   0 = everything completed successfully
   1 = one or more operations need manual follow-up (details printed at the end)

 ENDPOINT: All calls run against the PRODUCTION Microsoft Graph endpoint (v1.0).
       Beta endpoints are BLOCKED unless you opt in via the `$allowUseOfBetaApis` flag in the
       CONFIGURATION section or the -AllowUseOfBetaApis switch. With beta blocked, any
       blueprint API not yet on v1.0 is reported as a manual Graph Explorer step rather than
       being called against a preview endpoint.

 GRAPH QUIRK worth knowing: the delegated permissions published by a service principal are
       exposed under DIFFERENT property names per API version -
           v1.0 -> oauth2PermissionScopes
           beta -> publishedPermissionScopes
       The script reads BOTH names (see Get-PublishedScopes), so it works on either endpoint.
       Reading only one name makes every resource look like it publishes no delegated
       permissions.
===============================================================================================
#>

[CmdletBinding()]
param(
    # appId (client ID) of the Agent Identity Blueprint application.
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$')]
    [string] $BlueprintAppId,

    # Tenant to sign in to. Optional if you are already connected via Connect-MgGraph.
    [string] $TenantId,

    # Optional external JSON file that replaces the built-in $ResourceConfiguration block.
    [string] $ConfigPath,

    # Microsoft Graph API version. 'v1.0' is the production endpoint and is the default.
    # Selecting 'beta' also requires -AllowUseOfBetaApis (or the config flag below).
    [ValidateSet('v1.0', 'beta')]
    [string] $GraphApiVersion = 'v1.0',

    # Permit the script to call Microsoft Graph /beta endpoints.
    # OFF by default: with this flag unset the script NEVER touches a beta API - if the
    # agentIdentityBlueprint APIs are not yet available on v1.0 in the tenant, the affected
    # call is reported as a manual Graph Explorer step instead of silently using beta.
    [switch] $AllowUseOfBetaApis,

    # Preview mode: print every request that would be sent, but change nothing.
    [switch] $WhatIfOnly,

    # Print the effective configuration and exit.
    [switch] $ShowConfig
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'


#===============================================================================================
# SECTION 1 - CONFIGURATION   <<<<<  EDIT THIS SECTION  >>>>>
#===============================================================================================
#
# One entry per resource API. Add, remove or edit entries to match what your agent needs.
#
#   Name             Friendly label used in the console output only.
#   AppId            The resource application's appId (client ID). Required.
#   DelegatedScopes  Delegated permission names (the "scp" claim) to declare and to grant
#                    admin consent for. Use @() for none.
#                    Use '*' to mean "every delegated scope this resource publishes".
#   AppRoles         Application permission names (the "roles" claim) to declare on the
#                    blueprint. Use @() for none.
#                    NOTE: app roles are declared here but are NOT auto-consented by this
#                    script; grant them via the portal or appRoleAssignments if needed.
#   InheritScopes    'allAllowed' -> agent identities inherit delegated scopes from this resource
#                    'none'       -> they inherit no delegated scopes from this resource
#   InheritRoles     'allAllowed' | 'none' - same idea, for application roles.
#   GrantConsent     $true  -> create/extend the OAuth2 delegated grant for DelegatedScopes
#                    $false -> declare the permissions only, do not consent
#
#-----------------------------------------------------------------------------------------------

$ResourceConfiguration = @(

    #-------------------------------------------------------------------------------------------
    # Microsoft Graph
    #-------------------------------------------------------------------------------------------
    @{
        Name            = 'Microsoft Graph'
        AppId           = '00000003-0000-0000-c000-000000000000'

        # All DELEGATED permissions. Add or remove lines as needed.
        DelegatedScopes = @(
            'Calendars.ReadWrite'
            'Calendars.ReadWrite.Shared'
            'Channel.ReadBasic.All'
            'ChannelMessage.Read.All'
            'ChannelMessage.Send'
            'ChannelSettings.Read.All'
            'Chat.ReadWrite'
            'Directory.Read.All'
            'Files.ReadWrite'
            'Group.Read.All'
            'Mail.ReadWrite'
            'Mail.ReadWrite.Shared'
            'Mail.Send'
            'OnlineMeetings.ReadWrite'
            'Subscription.Read.All'
            'Team.ReadBasic.All'
            'TeamMember.Read.All'
            'TeamSettings.Read.All'
            'User.Read'
            'User.Read.All'
        )

        # Application (app-only) permissions. None required today.
        AppRoles        = @()

        InheritScopes   = 'allAllowed'
        InheritRoles    = 'allAllowed'
        GrantConsent    = $true
    },

    #-------------------------------------------------------------------------------------------
    # SMBA
    #-------------------------------------------------------------------------------------------
    @{
        Name            = 'SMBA'
        AppId           = '5a807f24-c9de-44ee-a3a7-329e88a00ffc'

        # All DELEGATED permissions.
        # Tip: use @('*') to declare and consent to every delegated scope SMBA publishes.
        DelegatedScopes = @(
            'AgentData.ReadWrite'
        )

        # Application (app-only) permissions. None required today.
        AppRoles        = @()

        InheritScopes   = 'allAllowed'
        InheritRoles    = 'allAllowed'
        GrantConsent    = $true
    }

    # ------------------------------------------------------------------------------------------
    # Add more resources here, for example SharePoint Online:
    #
    # ,@{
    #     Name            = 'SharePoint Online'
    #     AppId           = '00000003-0000-0ff1-ce00-000000000000'
    #     DelegatedScopes = @('AllSites.Read')
    #     AppRoles        = @()
    #     InheritScopes   = 'allAllowed'
    #     InheritRoles    = 'none'
    #     GrantConsent    = $true
    # }
    # ------------------------------------------------------------------------------------------
)

# Graph endpoints. All calls use the production (v1.0) endpoint.
#
# allowUseOfBetaApis
# ------------------
# The agentIdentityBlueprint APIs are rolling out to v1.0. If they are not yet available on
# v1.0 in this tenant, the script can retry ONLY that call against /beta.
#
#   $false (default) - never call a beta endpoint. Any blueprint API missing from v1.0 is
#                      reported as a manual Graph Explorer step for an administrator.
#   $true            - allow the automatic /beta retry. The script prints a clear notice
#                      every time it actually falls back.
#
# Set it here, or override at run time with the -AllowUseOfBetaApis switch.
# NOTE: this variable is deliberately NOT named $AllowUseOfBetaApis - PowerShell variable names
#       are case-insensitive, so that name would collide with the switch parameter above.
$allowUseOfBetaApisDefault = $false

#-----------------------------------------------------------------------------------------------

# Command-line switch wins over the config default above.
$UseBetaApis = $AllowUseOfBetaApis.IsPresent -or $allowUseOfBetaApisDefault

if ($GraphApiVersion -eq 'beta' -and -not $UseBetaApis) {
    throw "-GraphApiVersion 'beta' requires -AllowUseOfBetaApis (or set `$allowUseOfBetaApisDefault = `$true in the CONFIGURATION section)."
}

$GraphBaseUri = "https://graph.microsoft.com/$GraphApiVersion"

# Only populated when beta use is permitted; otherwise there is no fallback endpoint at all.
$GraphFallbackUri = $null
if ($UseBetaApis) {
    $GraphFallbackUri = if ($GraphApiVersion -eq 'v1.0') { 'https://graph.microsoft.com/beta' } else { 'https://graph.microsoft.com/v1.0' }
}

# Delegated scopes this script itself needs in order to run.
$ScriptRequiredScopes = @(
    'Application.ReadWrite.All'
    'AgentIdentityBlueprint.ReadWrite.All'
    'DelegatedPermissionGrant.ReadWrite.All'
    'Directory.ReadWrite.All'
)

#===============================================================================================
# END OF CONFIGURATION - no changes needed below this line
#===============================================================================================


#===============================================================================================
# SECTION 2 - CONSOLE OUTPUT HELPERS
#===============================================================================================

$script:ManualSteps      = [System.Collections.Generic.List[object]]::new()
$script:ResourceFailures = [System.Collections.Generic.List[object]]::new()

function Write-Banner {
    param([string] $Text)
    Write-Host ''
    Write-Host ('=' * 95) -ForegroundColor Cyan
    Write-Host " $Text" -ForegroundColor Cyan
    Write-Host ('=' * 95) -ForegroundColor Cyan
}

function Write-Phase {
    param([string] $Text)
    Write-Host ''
    Write-Host "--- $Text " -ForegroundColor Cyan -NoNewline
    Write-Host ('-' * [Math]::Max(0, 90 - $Text.Length)) -ForegroundColor Cyan
}

function Write-Ok     { param([string] $m) Write-Host "  [OK]      $m" -ForegroundColor Green }
function Write-Skipped{ param([string] $m) Write-Host "  [SKIP]    $m" -ForegroundColor DarkGray }
function Write-Warned { param([string] $m) Write-Host "  [WARN]    $m" -ForegroundColor Yellow }
function Write-Failed { param([string] $m) Write-Host "  [FAIL]    $m" -ForegroundColor Red }
function Write-Info   { param([string] $m) Write-Host "  [INFO]    $m" -ForegroundColor Gray }
function Write-Preview{ param([string] $m) Write-Host "  [PREVIEW] $m" -ForegroundColor Magenta }

<#
    Runs a per-resource step and, on ANY unexpected error, reports exactly which resource and
    which phase failed instead of dumping a bare PowerShell error. Execution continues with the
    next resource so one bad resource cannot abort the whole run.

    Returns $true on success, $false if the step threw.
#>
function Invoke-ResourceStep {
    param(
        [Parameter(Mandatory)] $Resource,
        [Parameter(Mandatory)][string] $StepName,
        [Parameter(Mandatory)][scriptblock] $Action
    )

    $who = "$($Resource.Name) (appId $($Resource.AppId))"
    try {
        & $Action
        return $true
    } catch {
        Write-Host ''
        Write-Failed "$StepName FAILED for resource: $who"
        Write-Host "            Error   : $($_.Exception.Message)" -ForegroundColor Red
        $line = 0
        if ($_.InvocationInfo) { $line = [int](Get-Prop -Object $_.InvocationInfo -Name 'ScriptLineNumber' -Default 0) }
        if ($line -gt 0) { Write-Host "            Location: script line $line" -ForegroundColor DarkRed }
        Write-Host "            The run continues with the next resource; this resource was not fully configured." -ForegroundColor DarkRed
        Write-Host ''

        $script:ResourceFailures.Add([pscustomobject]@{
            Resource = $who
            Step     = $StepName
            Error    = $_.Exception.Message
        })
        return $false
    }
}


#===============================================================================================
# SECTION 3 - MICROSOFT GRAPH PLUMBING
#===============================================================================================

function ConvertTo-GraphJson {
    param([Parameter(Mandatory)] $Body)
    return ($Body | ConvertTo-Json -Depth 20)
}

<#
    Strict-mode-safe property accessor.

    Microsoft Graph omits properties entirely when they have no value (for example, a service
    principal that publishes no delegated scopes has no 'oauth2PermissionScopes' property at
    all). Under 'Set-StrictMode -Version Latest' touching such a property is a terminating
    error, so ALL Graph response properties must be read through this function.

    Works with both PSCustomObject and Hashtable/Dictionary responses.
#>
function Get-Prop {
    param(
        $Object,
        [Parameter(Mandatory)][string] $Name,
        $Default = $null
    )

    if ($null -eq $Object) { return $Default }

    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name) -and $null -ne $Object[$Name]) { return $Object[$Name] }
        return $Default
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($property -and $null -ne $property.Value) { return $property.Value }
    return $Default
}

<#
    Convenience wrapper: always returns an array for a collection-valued Graph property.
    The leading comma prevents PowerShell from unrolling an empty array into $null.
#>
function Get-PropArray {
    param($Object, [Parameter(Mandatory)][string] $Name)
    return , @(Get-Prop -Object $Object -Name $Name -Default @())
}

function Write-IndentedJson {
    param([string] $Json, [string] $Indent = '    ', [string] $Color = 'Gray')
    foreach ($line in ($Json -split "`r?`n")) { Write-Host "$Indent$line" -ForegroundColor $Color }
}

function Get-GraphStatusCode {
    param([Parameter(Mandatory)] $ErrorRecord)
    $ex = $ErrorRecord.Exception
    while ($ex) {
        if ($ex.PSObject.Properties.Name -contains 'Response' -and $ex.Response) {
            try { return [int] $ex.Response.StatusCode } catch { }
        }
        $ex = $ex.InnerException
    }
    return 0
}

function Get-GraphErrorMessage {
    param([Parameter(Mandatory)] $ErrorRecord)
    $detail = $null
    if ($ErrorRecord.ErrorDetails -and $ErrorRecord.ErrorDetails.Message) {
        $detail = $ErrorRecord.ErrorDetails.Message
        try {
            $parsed = $detail | ConvertFrom-Json
            if ($parsed.PSObject.Properties.Name -contains 'error') {
                $detail = "$($parsed.error.code): $($parsed.error.message)"
            }
        } catch { }
    }
    if (-not $detail) { $detail = $ErrorRecord.Exception.Message }
    return ($detail -replace '\s+', ' ').Trim()
}

<#
    Records and prints a copy/paste-ready Graph Explorer instruction for a failed operation.
#>
function Register-ManualStep {
    param(
        [Parameter(Mandatory)][string] $Description,
        [Parameter(Mandatory)][string] $Method,
        [Parameter(Mandatory)][string] $Uri,
        $Body,
        [string] $Reason
    )

    $json = if ($null -ne $Body) { ConvertTo-GraphJson -Body $Body } else { $null }

    $script:ManualSteps.Add([pscustomobject]@{
        Description = $Description
        Method      = $Method.ToUpperInvariant()
        Uri         = $Uri
        Body        = $json
        Reason      = $Reason
    })

    Write-Host ''
    Write-Host '  +------------------------ MANUAL ACTION REQUIRED ------------------------' -ForegroundColor Yellow
    Write-Host "  | $Description" -ForegroundColor Yellow
    if ($Reason) { Write-Host "  | Reason: $Reason" -ForegroundColor Yellow }
    Write-Host '  | Run this in Microsoft Graph Explorer -> https://aka.ms/ge' -ForegroundColor Yellow
    Write-Host '  +------------------------------------------------------------------------' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "    $($Method.ToUpperInvariant()) $Uri" -ForegroundColor White
    if ($json) {
        Write-Host '    Content-Type: application/json' -ForegroundColor White
        Write-Host ''
        Write-IndentedJson -Json $json -Indent '    ' -Color White
    }
    Write-Host ''
}

<#
    Read-only Graph request. Returns $null on HTTP 404; rethrows anything else.
#>
function Invoke-GraphGet {
    param([Parameter(Mandatory)][string] $Uri)
    Write-Verbose "GET $Uri"
    try {
        return Invoke-MgGraphRequest -Method GET -Uri $Uri -OutputType PSObject
    } catch {
        if ((Get-GraphStatusCode -ErrorRecord $_) -eq 404) { return $null }
        throw
    }
}

<#
    Mutating Graph request.
    Returns the response object on success.
    On failure it prints a Graph Explorer fallback and returns $null (it does NOT throw),
    so a single permission problem does not abort the whole run.
#>
function Invoke-GraphWrite {
    param(
        [Parameter(Mandatory)][ValidateSet('POST', 'PATCH', 'PUT', 'DELETE')][string] $Method,
        [Parameter(Mandatory)][string] $Uri,
        [Parameter(Mandatory)][string] $Description,
        $Body,
        [int[]] $TolerateStatus = @()
    )

    if ($WhatIfOnly) {
        Write-Preview "$Method $Uri"
        if ($null -ne $Body) { Write-IndentedJson -Json (ConvertTo-GraphJson -Body $Body) -Indent '            ' -Color Magenta }
        return $null
    }

    Write-Verbose "$Method $Uri"
    try {
        $params = @{ Method = $Method; Uri = $Uri; OutputType = 'PSObject' }
        if ($null -ne $Body) {
            $params['Body']        = ConvertTo-GraphJson -Body $Body
            $params['ContentType'] = 'application/json'
        }
        return Invoke-MgGraphRequest @params
    } catch {
        $status = Get-GraphStatusCode -ErrorRecord $_
        if ($TolerateStatus -contains $status) {
            Write-Verbose "$Method $Uri returned tolerated status $status"
            return $null
        }
        $reason = "HTTP $status - $(Get-GraphErrorMessage -ErrorRecord $_)"
        Write-Failed "$Description failed."
        Register-ManualStep -Description $Description -Method $Method -Uri $Uri -Body $Body -Reason $reason
        return $null
    }
}

function Connect-GraphIfNeeded {
    Write-Phase 'Connecting to Microsoft Graph'

    if (-not (Get-Module -ListAvailable -Name Microsoft.Graph.Authentication)) {
        throw 'The Microsoft Graph PowerShell SDK is not installed. Run:  Install-Module Microsoft.Graph -Scope CurrentUser'
    }
    Import-Module Microsoft.Graph.Authentication -ErrorAction Stop

    $context = $null
    try { $context = Get-MgContext } catch { }

    if (-not $context) {
        $connectArgs = @{ Scopes = $ScriptRequiredScopes; NoWelcome = $true }
        if ($TenantId) { $connectArgs['TenantId'] = $TenantId }
        Connect-MgGraph @connectArgs
        $context = Get-MgContext
    }

    if (-not $context) { throw 'Failed to establish a Microsoft Graph connection.' }

    Write-Ok "Signed in as '$($context.Account)'"
    Write-Info "Tenant: $($context.TenantId)"

    $missing = @($ScriptRequiredScopes | Where-Object { $context.Scopes -notcontains $_ })
    if ($missing.Count -gt 0) {
        Write-Warned "The current token is missing scope(s): $($missing -join ', ')"
        Write-Warned "If operations fail, reconnect with:  Connect-MgGraph -Scopes $($ScriptRequiredScopes -join ',')"
    }
}


#===============================================================================================
# SECTION 4 - DIRECTORY OBJECT HELPERS
#===============================================================================================

<#
    Returns the delegated permissions published by a service principal.

    IMPORTANT: this property has TWO names in Microsoft Graph.
        v1.0 : oauth2PermissionScopes
        beta : publishedPermissionScopes
    (See the beta servicePrincipal reference: "This property is named oauth2PermissionScopes
    in v1.0.")  Because this script must use /beta for the agentIdentityBlueprint APIs, it has
    to tolerate both spellings - otherwise the scopes silently appear to be missing.
#>
function Get-PublishedScopes {
    param($ServicePrincipal)

    $scopes = Get-PropArray -Object $ServicePrincipal -Name 'oauth2PermissionScopes'
    if ($scopes.Count -eq 0) {
        $scopes = Get-PropArray -Object $ServicePrincipal -Name 'publishedPermissionScopes'
    }
    return , $scopes
}

function Get-ServicePrincipalByAppId {
    param([Parameter(Mandatory)][string] $AppId)

    # Ask for the delegated-scopes property using the name appropriate to the endpoint, then
    # the other name, then fall back to fetching the full object without $select.
    $scopeProperty = if ($GraphApiVersion -eq 'v1.0') {
        @('oauth2PermissionScopes', 'publishedPermissionScopes')
    } else {
        @('publishedPermissionScopes', 'oauth2PermissionScopes')
    }

    $filter     = "`$filter=appId eq '$AppId'"
    $candidates = @(
        "$GraphBaseUri/servicePrincipals?$filter&`$select=id,appId,displayName,appRoles,$($scopeProperty[0])"
        "$GraphBaseUri/servicePrincipals?$filter&`$select=id,appId,displayName,appRoles,$($scopeProperty[1])"
        "$GraphBaseUri/servicePrincipals?$filter"
    )

    foreach ($uri in $candidates) {
        try {
            $result = Invoke-GraphGet -Uri $uri
        } catch {
            $status = Get-GraphStatusCode -ErrorRecord $_
            if ($status -eq 400) {
                Write-Verbose "Endpoint rejected `$select for this API version; trying the next form."
                continue
            }
            throw
        }

        $values = Get-PropArray -Object $result -Name 'value'
        if ($values.Count -eq 0) { return $null }   # SP genuinely does not exist

        $sp = $values[0]
        # If the chosen $select produced no scopes, retry with the other spelling before
        # concluding that the resource publishes none.
        if ((Get-PublishedScopes -ServicePrincipal $sp).Count -eq 0 -and $uri -ne $candidates[-1]) {
            Write-Verbose 'No delegated scopes returned for this $select; retrying with the alternate property name.'
            continue
        }
        return $sp
    }

    return $null
}

<#
    Returns the service principal for $AppId, creating it if it does not exist.
    Returns $null if it is missing and creation failed (a manual step is printed in that case).
#>
function Resolve-ServicePrincipal {
    param(
        [Parameter(Mandatory)][string] $AppId,
        [Parameter(Mandatory)][string] $Label
    )

    $sp = Get-ServicePrincipalByAppId -AppId $AppId
    if ($sp) {
        Write-Ok "$Label service principal found - '$(Get-Prop -Object $sp -Name 'displayName' -Default $AppId)' (objectId $(Get-Prop -Object $sp -Name 'id'))"
        return $sp
    }

    Write-Warned "$Label service principal (appId $AppId) does not exist in this tenant. Creating it..."
    $created = Invoke-GraphWrite -Method POST `
                                -Uri  "$GraphBaseUri/servicePrincipals" `
                                -Body @{ appId = $AppId } `
                                -Description "Create the $Label service principal (appId $AppId)"

    if (-not $created) {
        # Either creation failed (fallback printed) or we are in preview mode. Re-probe once.
        $sp = Get-ServicePrincipalByAppId -AppId $AppId
        if ($sp) { Write-Ok "$Label service principal now present (objectId $(Get-Prop -Object $sp -Name 'id'))"; return $sp }
        return $null
    }

    Write-Ok "$Label service principal created (objectId $(Get-Prop -Object $created -Name 'id'))"
    return (Get-ServicePrincipalByAppId -AppId $AppId)   # re-read to pick up scopes / roles
}

function Resolve-BlueprintApplication {
    param([Parameter(Mandatory)][string] $AppId)

    $select = 'id,appId,displayName,requiredResourceAccess'
    $uri    = "$GraphBaseUri/applications?`$filter=appId eq '$AppId'&`$select=$select"
    $result = Invoke-GraphGet -Uri $uri

    $values = Get-PropArray -Object $result -Name 'value'
    if ($values.Count -gt 0) { return $values[0] }

    throw "No application registration was found with appId '$AppId'. Create the Agent Identity Blueprint first, then re-run this script."
}


#===============================================================================================
# SECTION 5 - CONFIGURATION NORMALISATION
#===============================================================================================

<#
    Applies defaults and validates a single resource configuration entry.
#>
function ConvertTo-NormalizedResource {
    param([Parameter(Mandatory)] $Entry, [int] $Index)

    function Get-Value {
        param($Source, [string] $Key, $Default)
        if ($Source -is [hashtable]) {
            if ($Source.ContainsKey($Key) -and $null -ne $Source[$Key]) { return $Source[$Key] }
        } elseif ($Source.PSObject.Properties.Name -contains $Key -and $null -ne $Source.$Key) {
            return $Source.$Key
        }
        return $Default
    }

    $appId = [string](Get-Value -Source $Entry -Key 'AppId' -Default '')
    if ([string]::IsNullOrWhiteSpace($appId)) {
        throw "Resource configuration entry #$($Index + 1) is missing the required 'AppId' property."
    }
    if ($appId -notmatch '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') {
        throw "Resource configuration entry #$($Index + 1) has an invalid 'AppId' value '$appId'. It must be a GUID."
    }

    $inheritScopes = [string](Get-Value -Source $Entry -Key 'InheritScopes' -Default 'allAllowed')
    $inheritRoles  = [string](Get-Value -Source $Entry -Key 'InheritRoles'  -Default 'allAllowed')
    foreach ($pair in @(@('InheritScopes', $inheritScopes), @('InheritRoles', $inheritRoles))) {
        if ($pair[1] -notin @('allAllowed', 'none')) {
            throw "Resource '$appId' has an invalid '$($pair[0])' value '$($pair[1])'. Use 'allAllowed' or 'none'."
        }
    }

    return [pscustomobject]@{
        Name            = [string](Get-Value -Source $Entry -Key 'Name' -Default $appId)
        AppId           = $appId
        DelegatedScopes = @(Get-Value -Source $Entry -Key 'DelegatedScopes' -Default @())
        AppRoles        = @(Get-Value -Source $Entry -Key 'AppRoles'        -Default @())
        InheritScopes   = $inheritScopes
        InheritRoles    = $inheritRoles
        GrantConsent    = [bool](Get-Value -Source $Entry -Key 'GrantConsent' -Default $true)
        # Populated during execution:
        ServicePrincipal   = $null
        ResolvedScopeNames = @()
    }
}

function Get-EffectiveConfiguration {
    if ($ConfigPath) {
        if (-not (Test-Path -LiteralPath $ConfigPath)) { throw "Config file not found: $ConfigPath" }
        Write-Info "Loading resource configuration from '$ConfigPath'"
        $raw = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
        # Accept either a bare array or an object with a 'Resources' property.
        if ($raw.PSObject.Properties.Name -contains 'Resources') { $raw = $raw.Resources }
        $entries = @($raw)
    } else {
        $entries = @($ResourceConfiguration)
    }

    if ($entries.Count -eq 0) { throw 'The resource configuration is empty. Nothing to do.' }

    $normalized = @()
    for ($i = 0; $i -lt $entries.Count; $i++) {
        $normalized += (ConvertTo-NormalizedResource -Entry $entries[$i] -Index $i)
    }

    $duplicate = $normalized | Group-Object AppId | Where-Object { $_.Count -gt 1 }
    if ($duplicate) { throw "Duplicate resource AppId(s) in configuration: $(($duplicate.Name) -join ', ')" }
    if ($normalized.Count -gt 50) { throw 'A blueprint supports a maximum of 50 resource apps.' }

    return $normalized
}

function Show-Configuration {
    param([Parameter(Mandatory)] $Resources)

    Write-Phase 'Effective configuration'
    foreach ($r in $Resources) {
        Write-Host ''
        Write-Host "  $($r.Name)  ($($r.AppId))" -ForegroundColor White
        $scopeText = if ($r.DelegatedScopes.Count -eq 0) { '(none)' } else { $r.DelegatedScopes -join ', ' }
        $roleText  = if ($r.AppRoles.Count -eq 0)        { '(none)' } else { $r.AppRoles -join ', ' }
        Write-Host "      Delegated scopes : $scopeText"
        Write-Host "      App roles        : $roleText"
        Write-Host "      Inherit scopes   : $($r.InheritScopes)"
        Write-Host "      Inherit roles    : $($r.InheritRoles)"
        Write-Host "      Grant consent    : $($r.GrantConsent)"
    }
}


#===============================================================================================
# SECTION 6 - PERMISSION RESOLUTION
#===============================================================================================

<#
    Expands '*' and resolves friendly permission names to the IDs published by the resource SP.
    Sets $Resource.ResolvedScopeNames and returns the resourceAccess entries for
    requiredResourceAccess.
#>
function Resolve-ResourcePermissions {
    param([Parameter(Mandatory)] $Resource)

    $sp              = $Resource.ServicePrincipal
    $publishedScopes = Get-PublishedScopes -ServicePrincipal $sp
    $publishedRoles  = Get-PropArray      -Object $sp -Name 'appRoles'

    # --- delegated scopes -------------------------------------------------------------------
    $wantedScopes = @($Resource.DelegatedScopes | Where-Object { $_ })

    if ($wantedScopes -contains '*') {
        $wantedScopes = @(foreach ($s in $publishedScopes) { Get-Prop -Object $s -Name 'value' }) | Where-Object { $_ }
        $wantedScopes = @($wantedScopes)
        Write-Info "'*' expanded to all $($wantedScopes.Count) delegated scope(s) published by $($Resource.Name)."
    }

    # A service principal that was just created (or whose publisher exposes nothing to this
    # tenant) reports no scopes. We cannot resolve permission IDs in that case, but the OAuth2
    # grant works off scope NAMES, so it can still be attempted.
    if ($publishedScopes.Count -eq 0 -and $wantedScopes.Count -gt 0) {
        Write-Warned "$($Resource.Name): the service principal returned no delegated permissions under either 'oauth2PermissionScopes' (v1.0) or 'publishedPermissionScopes' (beta)."
        Write-Warned "$($Resource.Name): this usually means the resource genuinely exposes no delegated scopes in this tenant, or the service principal was just created."
        Write-Warned "$($Resource.Name): skipping requiredResourceAccess for delegated scopes; the OAuth2 grant will still be attempted by scope name."
        $Resource.ResolvedScopeNames = $wantedScopes
        return , @()
    }

    $entries       = @()
    $resolvedNames = @()

    foreach ($name in $wantedScopes) {
        $match = @($publishedScopes | Where-Object { (Get-Prop -Object $_ -Name 'value') -eq $name })
        if ($match.Count -eq 0) {
            Write-Warned "Delegated scope '$name' is not published by $($Resource.Name) - skipped."
            continue
        }
        $entries       += @{ id = (Get-Prop -Object $match[0] -Name 'id'); type = 'Scope' }
        $resolvedNames += $name
    }

    # --- application roles ------------------------------------------------------------------
    foreach ($name in @($Resource.AppRoles | Where-Object { $_ })) {
        $match = @($publishedRoles | Where-Object {
            (Get-Prop -Object $_ -Name 'value') -eq $name -and
            (Get-PropArray -Object $_ -Name 'allowedMemberTypes') -contains 'Application'
        })
        if ($match.Count -eq 0) {
            Write-Warned "App role '$name' is not published by $($Resource.Name) - skipped."
            continue
        }
        $entries += @{ id = (Get-Prop -Object $match[0] -Name 'id'); type = 'Role' }
    }

    $Resource.ResolvedScopeNames = $resolvedNames
    return , $entries
}

<#
    Rebuilds the blueprint's requiredResourceAccess collection, merging in the new entries.
    Returns $null if nothing would change.
#>
function Merge-RequiredResourceAccess {
    param(
        $Existing,
        [Parameter(Mandatory)][string] $ResourceAppId,
        [Parameter(Mandatory)][AllowEmptyCollection()][array] $NewAccess
    )

    if ($NewAccess.Count -eq 0) { return $null }

    # Deep-copy the existing collection into plain hashtables we can safely mutate.
    $collection = @()
    foreach ($rra in @($Existing)) {
        if (-not $rra) { continue }

        # NOTE: assign Get-PropArray to a variable before enumerating. Piping its result
        # directly would pass the whole array as a single pipeline item.
        $access = Get-PropArray -Object $rra -Name 'resourceAccess'

        $collection += @{
            resourceAppId  = (Get-Prop -Object $rra -Name 'resourceAppId')
            resourceAccess = @(
                foreach ($item in $access) {
                    @{ id = (Get-Prop -Object $item -Name 'id'); type = (Get-Prop -Object $item -Name 'type') }
                }
            )
        }
    }

    $target = $collection | Where-Object { $_.resourceAppId -eq $ResourceAppId } | Select-Object -First 1
    if (-not $target) {
        $target = @{ resourceAppId = $ResourceAppId; resourceAccess = @() }
        $collection += $target
    }

    $changed = $false
    foreach ($access in $NewAccess) {
        $already = @($target.resourceAccess | Where-Object { $_.id -eq $access.id -and $_.type -eq $access.type })
        if ($already.Count -eq 0) {
            $target.resourceAccess += $access
            $changed = $true
        }
    }

    if (-not $changed) { return $null }
    return , $collection
}


#===============================================================================================
# SECTION 7 - BLUEPRINT INHERITABLE PERMISSIONS
#===============================================================================================

<#
    Creates (or updates) the inheritablePermission entry for one resource app.

    Two URL shapes for the agentIdentityBlueprint cast segment exist, and a tenant may expose
    these APIs on v1.0, on beta, or both. Every combination is attempted (preferred endpoint
    first) before reporting failure:
        /applications/{id}/microsoft.graph.agentIdentityBlueprint/inheritablePermissions
        /applications/microsoft.graph.agentIdentityBlueprint/{id}/inheritablePermissions
#>
function Set-InheritablePermission {
    param(
        [Parameter(Mandatory)][string] $BlueprintObjectId,
        [Parameter(Mandatory)] $Resource
    )

    $body = @{
        resourceAppId     = $Resource.AppId
        inheritableScopes = if ($Resource.InheritScopes -eq 'allAllowed') {
                                @{ '@odata.type' = '#microsoft.graph.allAllowedScopes'; kind = 'allAllowed' }
                            } else {
                                @{ '@odata.type' = '#microsoft.graph.noScopes';         kind = 'none' }
                            }
        inheritableRoles  = if ($Resource.InheritRoles -eq 'allAllowed') {
                                @{ '@odata.type' = '#microsoft.graph.allAllowedRoles';  kind = 'allAllowed' }
                            } else {
                                @{ '@odata.type' = '#microsoft.graph.noRoles';          kind = 'none' }
                            }
    }

    # The agentIdentityBlueprint cast segment has two documented URL shapes. Both are tried on
    # the primary endpoint; the fallback endpoint is only included when beta use is permitted
    # (see $allowUseOfBetaApis / -AllowUseOfBetaApis).
    $bases = @($GraphBaseUri)
    if ($GraphFallbackUri) { $bases += $GraphFallbackUri }

    $candidateUris = @()
    foreach ($base in $bases) {
        $candidateUris += "$base/applications/$BlueprintObjectId/microsoft.graph.agentIdentityBlueprint/inheritablePermissions"
        $candidateUris += "$base/applications/microsoft.graph.agentIdentityBlueprint/$BlueprintObjectId/inheritablePermissions"
    }

    if ($WhatIfOnly) {
        Write-Preview "POST $($candidateUris[0])"
        Write-IndentedJson -Json (ConvertTo-GraphJson -Body $body) -Indent '            ' -Color Magenta
        return
    }

    $lastReason = 'No response received.'

    foreach ($uri in $candidateUris) {
        Write-Verbose "POST $uri"
        try {
            $null = Invoke-MgGraphRequest -Method POST -Uri $uri `
                        -Body (ConvertTo-GraphJson -Body $body) `
                        -ContentType 'application/json' -OutputType PSObject

            Write-Ok "$($Resource.Name): inheritable permissions set (scopes=$($Resource.InheritScopes), roles=$($Resource.InheritRoles))"
            if ($uri -notlike "$GraphBaseUri/*") {
                Write-Info "$($Resource.Name): this tenant served inheritablePermissions from $GraphFallbackUri, not $GraphBaseUri."
            }
            return
        } catch {
            $status = Get-GraphStatusCode -ErrorRecord $_

            if ($status -eq 409) {
                # An entry already exists for this resourceAppId -> update it instead.
                $patchBody = @{ inheritableScopes = $body.inheritableScopes; inheritableRoles = $body.inheritableRoles }
                $patched = Invoke-GraphWrite -Method PATCH -Uri "$uri/$($Resource.AppId)" -Body $patchBody `
                            -Description "Update inheritable permissions for $($Resource.Name) on blueprint $BlueprintObjectId"
                if ($patched) {
                    Write-Ok "$($Resource.Name): inheritable permissions updated (entry already existed)"
                }
                return
            }

            $lastReason = "HTTP $status - $(Get-GraphErrorMessage -ErrorRecord $_)"
            if ($status -eq 400 -or $status -eq 404) {
                Write-Verbose "URL shape rejected ($lastReason); trying the alternate shape."
                continue
            }
            break
        }
    }

    Write-Failed "$($Resource.Name): setting inheritable permissions failed."
    if (-not $UseBetaApis) {
        Write-Info "Beta APIs are disabled. If these APIs are not yet on v1.0 in this tenant, re-run with -AllowUseOfBetaApis."
    }
    Register-ManualStep -Description "Add inheritable permissions for $($Resource.Name) on blueprint $BlueprintObjectId" `
                        -Method 'POST' -Uri $candidateUris[0] -Body $body -Reason $lastReason
}


#===============================================================================================
# SECTION 8 - OAUTH2 DELEGATED PERMISSION GRANTS
#===============================================================================================

<#
    Creates or extends the tenant-wide (AllPrincipals) delegated permission grant between
    the blueprint service principal (client) and a resource service principal.
#>
function Set-OAuth2PermissionGrant {
    param(
        [Parameter(Mandatory)][string] $ClientSpId,
        [Parameter(Mandatory)][string] $ResourceSpId,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Scopes,
        [Parameter(Mandatory)][string] $Label
    )

    $scopes = @($Scopes | Where-Object { $_ } | Select-Object -Unique)
    if ($scopes.Count -eq 0) {
        Write-Skipped "$Label - no delegated scopes to grant."
        return
    }

    $filter    = "clientId eq '$ClientSpId' and resourceId eq '$ResourceSpId'"
    $lookupUri = "$GraphBaseUri/oauth2PermissionGrants?`$filter=$filter"
    $found     = Invoke-GraphGet -Uri $lookupUri

    $grants   = Get-PropArray -Object $found -Name 'value'
    $existing = @($grants | Where-Object { (Get-Prop -Object $_ -Name 'consentType') -eq 'AllPrincipals' }) |
                Select-Object -First 1

    # ---- update an existing grant -----------------------------------------------------------
    if ($existing) {
        $current      = @()
        $currentScope = Get-Prop -Object $existing -Name 'scope'
        if ($currentScope) { $current = @($currentScope -split '\s+' | Where-Object { $_ }) }

        $missing = @($scopes | Where-Object { $current -notcontains $_ })
        if ($missing.Count -eq 0) {
            Write-Skipped "$Label - grant already contains all requested scopes."
            return
        }

        $merged  = (@($current + $missing) | Select-Object -Unique) -join ' '
        $updated = Invoke-GraphWrite -Method PATCH `
                        -Uri  "$GraphBaseUri/oauth2PermissionGrants/$(Get-Prop -Object $existing -Name 'id')" `
                        -Body @{ scope = $merged } `
                        -Description "Extend the OAuth2 grant for $Label (adding: $($missing -join ', '))"

        if ($updated) { Write-Ok "$Label - grant extended with: $($missing -join ', ')" }
        return
    }

    # ---- create a new grant -----------------------------------------------------------------
    $body = @{
        clientId    = $ClientSpId
        consentType = 'AllPrincipals'
        principalId = $null
        resourceId  = $ResourceSpId
        scope       = ($scopes -join ' ')
    }

    $created = Invoke-GraphWrite -Method POST `
                    -Uri  "$GraphBaseUri/oauth2PermissionGrants" `
                    -Body $body `
                    -Description "Create the OAuth2 delegated grant for $Label"

    if ($created) { Write-Ok "$Label - grant created: $($body.scope)" }
}


#===============================================================================================
# SECTION 9 - MAIN EXECUTION
#===============================================================================================

Write-Banner 'Agent Identity Blueprint configuration'
Write-Host "  Blueprint appId : $BlueprintAppId"
Write-Host "  Graph endpoint  : $GraphBaseUri$(if ($GraphApiVersion -eq 'v1.0') { '  (production)' } else { '  (preview)' })"
if ($UseBetaApis) {
    Write-Host "  Beta APIs       : ALLOWED - may fall back to $GraphFallbackUri for blueprint APIs" -ForegroundColor Yellow
} else {
    Write-Host "  Beta APIs       : blocked (allowUseOfBetaApis = false)"}
Write-Host "  Mode            : $(if ($WhatIfOnly) { 'PREVIEW (no changes will be made)' } else { 'APPLY' })"

$resources = Get-EffectiveConfiguration
Show-Configuration -Resources $resources

if ($ShowConfig) {
    Write-Host ''
    Write-Host '  -ShowConfig specified: exiting without contacting Microsoft Graph.' -ForegroundColor Gray
    exit 0
}

Connect-GraphIfNeeded

#-----------------------------------------------------------------------------------------------
# PHASE 1 - locate the blueprint application
#-----------------------------------------------------------------------------------------------
Write-Phase 'PHASE 1  Locate the Agent Identity Blueprint application'

$blueprintApp      = Resolve-BlueprintApplication -AppId $BlueprintAppId
$blueprintObjectId = Get-Prop -Object $blueprintApp -Name 'id'
Write-Ok "Blueprint '$(Get-Prop -Object $blueprintApp -Name 'displayName' -Default $BlueprintAppId)' found (objectId $blueprintObjectId)"

#-----------------------------------------------------------------------------------------------
# PHASE 2 - ensure every RESOURCE service principal exists   [covers requirement 5 for SMBA]
#-----------------------------------------------------------------------------------------------
Write-Phase 'PHASE 2  Ensure resource service principals exist'

foreach ($resource in $resources) {
    $null = Invoke-ResourceStep -Resource $resource -StepName 'PHASE 2 (resolve service principal)' -Action {
        $resource.ServicePrincipal = Resolve-ServicePrincipal -AppId $resource.AppId -Label $resource.Name
        if (-not $resource.ServicePrincipal) {
            Write-Warned "$($resource.Name): service principal unavailable - dependent steps will be skipped."
        }
    }
}

#-----------------------------------------------------------------------------------------------
# PHASE 3 - add resource permissions to the blueprint        [requirement 1]
#-----------------------------------------------------------------------------------------------
Write-Phase 'PHASE 3  Add resource permissions to the blueprint (requiredResourceAccess)'

$existingRra = Get-PropArray -Object $blueprintApp -Name 'requiredResourceAccess'

$pendingRra = $existingRra
$rraChanged = $false

foreach ($resource in $resources) {
    if (-not $resource.ServicePrincipal) {
        Write-Skipped "$($resource.Name): service principal unavailable - permissions not staged."
        continue
    }

    $null = Invoke-ResourceStep -Resource $resource -StepName 'PHASE 3 (resolve permissions / requiredResourceAccess)' -Action {

        $entries = Resolve-ResourcePermissions -Resource $resource
        if ($entries.Count -eq 0) {
            Write-Skipped "$($resource.Name): no permission IDs to stage."
            return
        }

        $merged = Merge-RequiredResourceAccess -Existing $pendingRra -ResourceAppId $resource.AppId -NewAccess $entries
        if ($merged) {
            $script:pendingRra = $merged
            $script:rraChanged = $true
            Write-Ok "$($resource.Name): $($entries.Count) permission(s) staged for the blueprint."
        } else {
            Write-Skipped "$($resource.Name): all configured permissions are already declared."
        }
    }
}

if ($rraChanged) {
    $patched = Invoke-GraphWrite -Method PATCH `
                    -Uri  "$GraphBaseUri/applications/$blueprintObjectId" `
                    -Body @{ requiredResourceAccess = $pendingRra } `
                    -Description "Add requiredResourceAccess to the blueprint (objectId $blueprintObjectId)"
    if ($patched) { Write-Ok 'Blueprint requiredResourceAccess updated.' }
} else {
    Write-Skipped 'No requiredResourceAccess changes were needed.'
}

#-----------------------------------------------------------------------------------------------
# PHASE 4 - add inheritable permissions to the blueprint     [requirement 2]
#-----------------------------------------------------------------------------------------------
Write-Phase 'PHASE 4  Add inheritable permissions to the blueprint'

foreach ($resource in $resources) {
    $null = Invoke-ResourceStep -Resource $resource -StepName 'PHASE 4 (inheritable permissions)' -Action {
        Set-InheritablePermission -BlueprintObjectId $blueprintObjectId -Resource $resource
    }
}

#-----------------------------------------------------------------------------------------------
# PHASE 5 - ensure the BLUEPRINT service principal exists    [requirements 3 & 4]
#-----------------------------------------------------------------------------------------------
Write-Phase 'PHASE 5  Ensure the Agent Identity Blueprint service principal exists'

$blueprintSp = Resolve-ServicePrincipal -AppId $BlueprintAppId -Label 'Agent Identity Blueprint'

#-----------------------------------------------------------------------------------------------
# PHASE 6 - create the OAuth2 delegated grants               [requirements 6 & 7]
#-----------------------------------------------------------------------------------------------
Write-Phase 'PHASE 6  Create OAuth2 delegated permission grants (admin consent)'

foreach ($resource in $resources) {

    if (-not $resource.GrantConsent) {
        Write-Skipped "$($resource.Name): GrantConsent is disabled in configuration."
        continue
    }

    $null = Invoke-ResourceStep -Resource $resource -StepName 'PHASE 6 (OAuth2 delegated grant)' -Action {

        $label = "blueprint -> $($resource.Name)"

        if ($blueprintSp -and $resource.ServicePrincipal) {
            Set-OAuth2PermissionGrant -ClientSpId   (Get-Prop -Object $blueprintSp -Name 'id') `
                                      -ResourceSpId (Get-Prop -Object $resource.ServicePrincipal -Name 'id') `
                                      -Scopes       $resource.ResolvedScopeNames `
                                      -Label        $label
            return
        }

        # A required service principal is missing -> emit the manual fallback.
        $scopeText = if (@($resource.ResolvedScopeNames).Count -gt 0) {
                         $resource.ResolvedScopeNames -join ' '
                     } else {
                         (@($resource.DelegatedScopes | Where-Object { $_ -and $_ -ne '*' }) -join ' ')
                     }

        $reason = 'The blueprint service principal and/or the resource service principal could not be resolved.'
        Write-Failed "$label - $reason"

        Register-ManualStep -Description "Create the OAuth2 delegated grant: $label" `
            -Method 'POST' -Uri "$GraphBaseUri/oauth2PermissionGrants" -Reason $reason `
            -Body @{
                clientId    = $(if ($blueprintSp) { Get-Prop -Object $blueprintSp -Name 'id' } else { '<objectId of the Agent Identity Blueprint service principal>' })
                consentType = 'AllPrincipals'
                principalId = $null
                resourceId  = $(if ($resource.ServicePrincipal) { Get-Prop -Object $resource.ServicePrincipal -Name 'id' } else { "<objectId of the $($resource.Name) service principal (appId $($resource.AppId))>" })
                scope       = $scopeText
            }
    }
}


#===============================================================================================
# SECTION 10 - SUMMARY
#===============================================================================================

Write-Banner 'Summary'

if ($script:ResourceFailures.Count -gt 0) {
    Write-Host "  $($script:ResourceFailures.Count) resource(s) hit an unexpected error:" -ForegroundColor Red
    foreach ($failure in $script:ResourceFailures) {
        Write-Host ''
        Write-Host "    Resource : $($failure.Resource)" -ForegroundColor Red
        Write-Host "    Step     : $($failure.Step)"     -ForegroundColor Red
        Write-Host "    Error    : $($failure.Error)"    -ForegroundColor Red
    }
    Write-Host ''
}

if ($WhatIfOnly) {
    Write-Host '  PREVIEW MODE - no changes were made. Re-run without -WhatIfOnly to apply.' -ForegroundColor Magenta
    Write-Host ''
    exit $(if ($script:ResourceFailures.Count -gt 0) { 1 } else { 0 })
}

if ($script:ManualSteps.Count -eq 0 -and $script:ResourceFailures.Count -eq 0) {
    Write-Host '  All operations completed successfully.' -ForegroundColor Green
    Write-Host ''
    exit 0
}

if ($script:ManualSteps.Count -eq 0) {
    Write-Host '  No manual Graph Explorer steps were recorded, but see the resource error(s) above.' -ForegroundColor Yellow
    Write-Host ''
    exit 1
}

Write-Host "  $($script:ManualSteps.Count) operation(s) could not be completed automatically." -ForegroundColor Yellow
Write-Host '  Ask a Microsoft Entra administrator to run the following in Graph Explorer:' -ForegroundColor Yellow
Write-Host '      https://developer.microsoft.com/graph/graph-explorer' -ForegroundColor Yellow

$index = 1
foreach ($step in $script:ManualSteps) {
    Write-Host ''
    Write-Host "  ($index) $($step.Description)" -ForegroundColor Yellow
    if ($step.Reason) { Write-Host "       Reason: $($step.Reason)" -ForegroundColor DarkYellow }
    Write-Host ''
    Write-Host "       $($step.Method) $($step.Uri)" -ForegroundColor White
    if ($step.Body) {
        Write-Host '       Content-Type: application/json' -ForegroundColor White
        Write-IndentedJson -Json $step.Body -Indent '       ' -Color White
    }
    $index++
}

Write-Host ''
exit 1
