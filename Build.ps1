param()

trap
{
    Write-Host "An error occurred"
    Write-Host $_
    Write-Host $_.Exception.StackTrace
    exit 1
}

$ErrorActionPreference = 'Stop'

$cwd = Get-Location

if (Test-path "$cwd\log.txt") {
    Write-host "Removing old log file..."
    Remove-item "$cwd\log.txt"
}

Write-Host "Starting Unity build..."
$unity_path = "C:\Program Files\Unity_5.4.1f\Editor\Unity.exe"
if (!(Test-Path $unity_path)) {
    $unity_path = "C:\Program Files\Unity\Editor\Unity.exe"
}
$process = (Start-Process -PassThru -FilePath $unity_path `
    -ArgumentList @(
        "-nographics",
        "-batchmode",
        "-projectPath",
        $cwd,
        "-buildWindowsPlayer",
        "$cwd\TestBuild\Game.exe",
        "-logFile",
        "$cwd\log.txt",
        "-quit"
    ))

$offset = 0;
while (-not $process.HasExited) {
    if (!(Test-Path "$cwd\log.txt")) {
        Write-Host "Waiting for $cwd\log.txt to appear..."
        Sleep -Milliseconds 1000
        continue
    }

    $content = (Get-Content -Raw "$cwd\log.txt")
    if ($offset -ge $content.Length) {
        Sleep -Milliseconds 500
        continue
    }
    $new_content = $content.Substring($offset)
    $offset = $content.Length
    Write-Host -NoNewline $new_content
}

# Write out any remaining content
if (Test-Path "$cwd\log.txt") {
    $content = (Get-Content -Raw "$cwd\log.txt")
    if ($offset -lt $content.Length) {
        $new_content = $content.Substring($offset)
        $offset = $content.Length
        Write-Host -NoNewline $new_content
    }
} else {
    Write-Host "error: No log file after Unity process exited!"
    exit 1
}

Write-Host "Unity executable finished running"

# Unity doesn't report success or failure with exit codes, we have to scan the file output...
$content = (Get-Content -Raw "$cwd\log.txt")
if ($content.Contains("Exiting batchmode successfully now")) {
    exit 0
} else {
    exit 1
}