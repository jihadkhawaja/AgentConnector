[CmdletBinding()]
param(
    [uri]$BaseUri = 'https://localhost:7188',
    [string]$ConnectionName = 'work',
    [string]$IssueKey,
    [string]$PageId
)

$ErrorActionPreference = 'Stop'
if (-not $BaseUri.IsAbsoluteUri -or $BaseUri.UserInfo -or $BaseUri.Query -or $BaseUri.Fragment -or
    $BaseUri.AbsolutePath -ne '/' -or
    ($BaseUri.Scheme -ne 'https' -and -not ($BaseUri.Scheme -eq 'http' -and $BaseUri.IsLoopback))) {
    throw 'BaseUri must be an HTTPS origin, or loopback HTTP for development.'
}
if ([string]::IsNullOrWhiteSpace($ConnectionName)) { throw 'ConnectionName is required.' }
if ([string]::IsNullOrWhiteSpace($env:Connector__RuntimeToken)) {
    throw 'Set Connector__RuntimeToken in this shell to the sample connector runtime token.'
}
$requestHeaders = @{ Authorization = "Bearer $env:Connector__RuntimeToken" }

function Invoke-ReadAction {
    param([string]$ActionId, [hashtable]$InputValues)
    $requestBody = @{ connectionName = $ConnectionName; input = $InputValues } | ConvertTo-Json -Depth 8 -Compress
    $actionUri = [uri]::new($BaseUri, "/connector/v1/actions/$ActionId")
    Invoke-RestMethod -Method Post -Uri $actionUri -Headers $requestHeaders -ContentType 'application/json' -Body $requestBody
}

Invoke-ReadAction -ActionId 'jira.get_current_user' -InputValues @{}
Invoke-ReadAction -ActionId 'jira.list_projects' -InputValues @{ startAt = 0 }
Invoke-ReadAction -ActionId 'confluence.list_spaces' -InputValues @{}
if ($IssueKey) { Invoke-ReadAction -ActionId 'jira.get_issue' -InputValues @{ issue_id = $IssueKey } }
if ($PageId) { Invoke-ReadAction -ActionId 'confluence.get_page' -InputValues @{ page_id = $PageId } }
