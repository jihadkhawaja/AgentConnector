[CmdletBinding()]
param([uri]$BaseUri = 'http://127.0.0.1:5190')

$ErrorActionPreference = 'Stop'
if (-not $env:Connector__AdminToken -or -not $env:Connector__RuntimeToken) {
    throw 'Set Connector__AdminToken and Connector__RuntimeToken to the values used by the sample host.'
}
if ($BaseUri.Scheme -ne 'https' -and -not ($BaseUri.Scheme -eq 'http' -and $BaseUri.IsLoopback)) {
    throw 'Use HTTPS, or loopback HTTP for the development sample.'
}
$adminHeaders = @{ Authorization = "Bearer $env:Connector__AdminToken" }
$runtimeHeaders = @{ Authorization = "Bearer $env:Connector__RuntimeToken" }
$alias = 'storage-smoke-' + [Guid]::NewGuid().ToString('N')
$connectionUri = [uri]::new($BaseUri, "/connector/api/connections/github?connectionName=$alias")
$listUri = [uri]::new($BaseUri, '/connector/v1/connections')

try {
    foreach ($secret in @('synthetic-storage-smoke-token', 'synthetic-updated-storage-smoke-token')) {
        $payload = @{ credential = @{ kind = 'bearer'; secret = $secret } } | ConvertTo-Json -Depth 4
        $result = Invoke-RestMethod -Uri $connectionUri -Method Put -Headers $adminHeaders -ContentType 'application/json' -Body $payload
        if (-not $result.success) { throw 'Saving the synthetic connection failed.' }
        $connections = Invoke-RestMethod -Uri $listUri -Headers $runtimeHeaders
        $selected = @($connections.data | Where-Object { $_.providerId -eq 'github' -and $_.name -eq $alias })
        if ($selected.Count -ne 1) { throw 'Expected exactly one synthetic connection.' }
        if (($connections | ConvertTo-Json -Depth 8) -match 'synthetic-.*storage-smoke-token') {
            throw 'Connection listing exposed a synthetic credential.'
        }
    }
}
finally {
    Invoke-RestMethod -Uri $connectionUri -Method Delete -Headers $adminHeaders | Out-Null
}
$connections = Invoke-RestMethod -Uri $listUri -Headers $runtimeHeaders
if (@($connections.data | Where-Object { $_.name -eq $alias }).Count -ne 0) { throw 'Deleting the synthetic connection failed.' }
Write-Output 'Storage smoke test passed: save, replace, metadata listing, and delete. No provider API was called.'
