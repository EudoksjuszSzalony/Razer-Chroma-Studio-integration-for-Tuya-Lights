# Read-only connection monitor for Hue bridge HTTPS discovery (TCP/443).
# Does not implement HTTPS or capture application payload, tokens or private keys.
# Separate from HueDiscoveryProbe, which continues to serve HTTP/80.
[CmdletBinding()]
param(
    [string]$Ip = "192.168.1.10",
    [ValidateRange(1, 65535)][int]$Port = 443,
    [ValidateRange(1, 600)][int]$Seconds = 150,
    [switch]$SelfTest
)
$ErrorActionPreference = "Stop"

if ($SelfTest) {
    $Ip = "127.0.0.1"
    $Port = 0  # Let Windows allocate an ephemeral free port.
    $Seconds = 3
}

$address = [System.Net.IPAddress]::Parse($Ip)
if ($address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "Only IPv4 is supported."
}
$listener = [System.Net.Sockets.TcpListener]::new($address, $Port)
$detected = 0
try {
    $listener.Start()
    $listenPort = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    if ($SelfTest) {
        $testClient = [System.Net.Sockets.TcpClient]::new()
        try {
            $testClient.Connect($address, $listenPort)
        } finally {
            $testClient.Close()
        }
    } else {
        Write-Host "[HTTPS probe] Listening for TCP connections to $($Ip):$listenPort for $Seconds seconds."
        Write-Host "[HTTPS probe] Start Synapse HUE pairing NOW. This port monitor does not answer TLS."
        Write-Host "[HTTPS probe] A detected TCP connection is evidence of an HTTPS-port attempt, not a successful TLS handshake."
        Write-Host "[HTTPS probe] Keep hue -Pairing running in a separate window (HTTP/80)."
    }

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
        if ($listener.Pending()) {
            $client = $listener.AcceptTcpClient()
            try {
                $detected++
                $remote = [string]$client.Client.RemoteEndPoint
                Write-Host "[HTTPS probe] TCP connection from $remote (total: $detected)"
            } finally {
                # Immediately close: no handshake, payload reading or persistence.
                $client.Close()
            }
        } else {
            Start-Sleep -Milliseconds 100
        }
        if ($SelfTest -and $detected -ge 1) { break }
    }
    if ($SelfTest) {
        if ($detected -ne 1) { throw "443 probe loopback self-test failed." }
        Write-Host "[HTTPS probe] loopback self-test: OK"
    } elseif ($detected -eq 0) {
        Write-Host "[HTTPS probe] No inbound TCP connections observed during the test."
        Write-Host "[HTTPS probe] This does not rule out different interfaces, other ports or other discovery mechanisms."
    } else {
        Write-Host "[HTTPS probe] Detected $detected connection(s). The next step is a real HTTPS Hue endpoint."
    }
} catch [System.Net.Sockets.SocketException] {
    throw "Cannot listen on $($Ip):$Port (port may be occupied or IP not assigned locally): $($_.Exception.Message)"
} finally {
    $listener.Stop()
}
