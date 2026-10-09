# Prompts for a word or phrase and sends it to the running game,
# which spawns it as text in front of the player (see TerminalTextSpawner.cs).
param(
    [int]$Port = 5055
)

$text = Read-Host "Input word or phrase"
if ([string]::IsNullOrWhiteSpace($text)) {
    Write-Host "Nothing entered."
    exit 1
}

try {
    $client = New-Object System.Net.Sockets.TcpClient("127.0.0.1", $Port)
} catch {
    Write-Host "Couldn't reach the game on port $Port. Is it running in Play mode?"
    exit 1
}

$writer = New-Object System.IO.StreamWriter($client.GetStream(), (New-Object System.Text.UTF8Encoding($false)))
$writer.WriteLine($text)
$writer.Flush()
$writer.Dispose()
$client.Dispose()

Write-Host "Sent: $text"
