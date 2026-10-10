# Read-only local prerequisites for native Chroma Connect RGB reception.
# No administrator permission, user secrets, device writes or external requests.
[CmdletBinding()]
param()

Write-Host "=== Chroma Connect local prerequisites (read-only) ==="
Write-Host "Windows: $([System.Environment]::OSVersion.VersionString)"
Write-Host "PowerShell: $($PSVersionTable.PSVersion)"

Write-Host "`nRazer/Chroma processes (names only):"
$processes = @(Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'Razer|Chroma|RzSynapse' } |
    Select-Object -ExpandProperty ProcessName -Unique)
if ($processes.Count -gt 0) {
    $processes | Sort-Object | ForEach-Object { Write-Host ("  " + $_) }
} else {
    Write-Host "  None visible. Start Synapse before running the actual probe."
}

Write-Host "`n.NET SDKs:"
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCommand) {
    & dotnet --list-sdks
} else {
    Write-Host "  dotnet is not in PATH (install the .NET 8 SDK to build ChromaProbe)."
}

Write-Host "`nLooking for RzChromaConnectAPI64.dll in standard Razer directories..."
$roots = @(
    [System.IO.Path]::Combine($env:ProgramFiles, "Razer"),
    [System.IO.Path]::Combine([System.Environment]::GetFolderPath("ProgramFilesX86"), "Razer"),
    [System.IO.Path]::Combine($env:ProgramData, "Razer"),
    [System.IO.Path]::Combine($env:LOCALAPPDATA, "Razer")
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) } | Select-Object -Unique

$matches = @()
foreach ($root in $roots) {
    $matches += @(Get-ChildItem -LiteralPath $root -Filter "RzChromaConnectAPI64.dll" -File -Recurse -ErrorAction SilentlyContinue)
}
if ($matches.Count -gt 0) {
    $matches | Select-Object -ExpandProperty FullName -Unique | ForEach-Object { Write-Host ("  Found: " + $_) }
} else {
    Write-Host "  Not found in standard Razer folders; this is not an exhaustive system search."
}
Write-Host "`nOpen Synapse 3 > Modules and confirm Chroma Connect is installed/enabled."
Write-Host "Native DLL availability does NOT imply access to an authorized partner application ID."
Write-Host "This check did not modify lighting or configuration."
