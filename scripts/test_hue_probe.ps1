# Windows Hue emulator smoke test. No real Tuya or Synapse connection.
$ErrorActionPreference = "Stop"
$exe = Join-Path (Get-Location) "src\HueDiscoveryProbe\bin\Release\net9.0\HueDiscoveryProbe.exe"
if (!(Test-Path $exe)) { throw "Hue emulator EXE not found after build" }
$process = Start-Process -FilePath $exe -ArgumentList @(
    "--ip", "127.0.0.1", "--port", "18080", "--no-ssdp", "--pairing-seconds", "60"
) -PassThru
$base = "http://127.0.0.1:18080"
try {
    $ready = $false
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 200
        try {
            $response = Invoke-WebRequest "$base/description.xml" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch {
            if ($process.HasExited) { throw "Hue emulator exited unexpectedly" }
        }
    }
    if (!$ready) { throw "Hue emulator failed to start HTTP" }
    if ($response.Content -notmatch "BSB002|Philips hue bridge") {
        throw "Hue description XML missing bridge model"
    }
    $config = Invoke-RestMethod "$base/api/nouser/config"
    if ($config.modelid -ne "BSB002") { throw "Hue config model mismatch" }
    $pair = Invoke-RestMethod -Uri "$base/api" -Method Post -ContentType "application/json" -Body '{"devicetype":"ci-smoke-test","generateclientkey":true}'
    $username = @($pair)[0].success.username
    $clientKey = @($pair)[0].success.clientkey
    if (!$username -or !$clientKey) { throw "Hue pairing failed" }
    $lights = Invoke-RestMethod "$base/api/$username/lights"
    if (!$lights.'1' -or $lights.'1'.state.on -ne $true) { throw "Virtual lamp missing" }
    $group = Invoke-RestMethod "$base/api/$username/groups/1"
    if ($group.type -ne "Entertainment" -or $group.lights[0] -ne "1") {
        throw "Entertainment group missing"
    }
    $null = Invoke-RestMethod -Uri "$base/api/$username/lights/1/state" -Method Put -ContentType "application/json" -Body '{"hue":25500,"sat":254,"bri":100}'
    $updated = Invoke-RestMethod "$base/api/$username/lights/1"
    if ($updated.state.hue -ne 25500) { throw "Virtual lamp memory update missing" }
    $stream = Invoke-RestMethod -Uri "$base/api/$username/groups/1/stream" -Method Put -ContentType "application/json" -Body '{"stream":{"active":true}}'
    if (@($stream)[0].error.type -ne 901) { throw "Entertainment stream should be rejected" }
    Write-Host "Hue mock bridge: HTTP, pairing, virtual light and stream rejection OK"
} finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
