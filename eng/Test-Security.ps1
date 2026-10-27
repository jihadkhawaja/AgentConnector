[CmdletBinding()]
param([switch]$PrepareVeracode)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$reportDirectory = Join-Path $repositoryRoot 'artifacts/security/reports'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null

function Invoke-DotNet {
    param([string[]]$CommandArguments)
    & dotnet @CommandArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($CommandArguments[0]) failed with exit code $LASTEXITCODE." }
}

Push-Location $repositoryRoot
try {
    # Re-evaluate the restore and fetch fresh advisory data. Audit failures are errors.
    Invoke-DotNet -CommandArguments @('restore', 'AgentConnector.slnx', '--locked-mode', '--force', '--no-http-cache')
    Invoke-DotNet -CommandArguments @('build', 'AgentConnector.slnx', '-c', 'Release', '--no-restore', '--no-incremental', "-p:SecurityReportDirectory=$reportDirectory")
    Invoke-DotNet -CommandArguments @('test', 'AgentConnector.slnx', '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx')

    $auditOutput = & dotnet list AgentConnector.slnx package --vulnerable --include-transitive --no-restore --format json
    if ($LASTEXITCODE -ne 0) { throw 'The dependency audit could not be completed.' }
    $auditOutput | Set-Content -LiteralPath (Join-Path $reportDirectory 'dependencies.json') -Encoding utf8
    $audit = $auditOutput | Out-String | ConvertFrom-Json
    if ($audit.problems) { throw 'The dependency audit reported an error; inspect dependencies.json.' }
    if (@($audit.projects).Count -ne 3) { throw 'The dependency audit did not report every solution project.' }
    foreach ($project in $audit.projects) {
        foreach ($framework in $project.frameworks) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($package.vulnerabilities) { throw "A vulnerable dependency was found in $($project.path); inspect dependencies.json." }
            }
        }
    }

    if ($PrepareVeracode) {
        # A unique, empty directory keeps stale files and local credentials out of the archive.
        $packageDirectory = Join-Path $repositoryRoot ('artifacts/security/veracode-' + [Guid]::NewGuid().ToString('N'))
        Invoke-DotNet -CommandArguments @('publish', 'samples/AgentConnector.Server/AgentConnector.Server.csproj', '-c', 'Debug', '--no-restore',
            '--self-contained', 'false', '-p:UseAppHost=false', '-p:DebugType=portable', '-p:Optimize=false', '-o', $packageDirectory)
        $scanFiles = @('AgentConnector.dll', 'AgentConnector.pdb', 'AgentConnector.Server.dll', 'AgentConnector.Server.pdb',
            'AgentConnector.Server.deps.json', 'AgentConnector.Server.runtimeconfig.json')
        $scanPaths = foreach ($scanFile in $scanFiles) {
            $scanPath = Join-Path $packageDirectory $scanFile
            if (-not (Test-Path -LiteralPath $scanPath -PathType Leaf)) { throw "Missing scan file: $scanFile" }
            $scanPath
        }
        $archivePath = Join-Path $reportDirectory 'veracode.zip'
        Compress-Archive -LiteralPath $scanPaths -DestinationPath $archivePath -Force
        Write-Output "Veracode upload package: $archivePath"
    }

    Write-Output "Security checks passed. Reports: $reportDirectory"
}
finally {
    Pop-Location
}
