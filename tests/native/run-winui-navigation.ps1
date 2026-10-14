param(
    [Parameter(Mandatory=$true)][string]$AppDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [int]$PreviewPort = 14976
)
$ErrorActionPreference = 'Stop'
$AppDirectory = [IO.Path]::GetFullPath($AppDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$exe = Join-Path $AppDirectory 'WidgetSamples.exe'
if (!(Test-Path $exe)) { $exe = Join-Path $AppDirectory 'dotnative-desktop.exe' }
if (!(Test-Path $exe) -or !(Test-Path "$AppDirectory\managed\DotNative.DevHost.exe")) {
    throw 'Expected a WinUI renderer with the WidgetSamples managed host in managed/.'
}
$oldPort = $env:DOTNATIVE_PREVIEW_PORT
$oldTest = $env:DOTNATIVE_WINUI_NAVIGATION_TEST
$oldWindowTest = $env:DOTNATIVE_WINUI_WINDOW_TEST
$process = $null
try {
    $env:DOTNATIVE_PREVIEW_PORT = "$PreviewPort"
    $env:DOTNATIVE_WINUI_NAVIGATION_TEST = '1'
    $env:DOTNATIVE_WINUI_WINDOW_TEST = '1'
    $process = Start-Process $exe -WorkingDirectory $AppDirectory -PassThru -RedirectStandardOutput "$OutputDirectory\app.log" -RedirectStandardError "$OutputDirectory\app.err"
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $output = Get-Content "$OutputDirectory\app.log" -Raw -ErrorAction SilentlyContinue
        $errors = Get-Content "$OutputDirectory\app.err" -Raw -ErrorAction SilentlyContinue
        if ($errors -match 'FAIL|Exception|fatal') { throw $errors }
        if ($output -and $output.Contains('PASS WidgetSamples radio selection/grouping/state + Home/Cart') -and $output.Contains('PASS native window move/resize loop')) {
            $result = ($output -split [Environment]::NewLine | Where-Object { $_ -like 'PASS *' }) -join [Environment]::NewLine
            $result | Set-Content "$OutputDirectory\result.txt"
            Write-Output $result
            exit 0
        }
        if ($process.HasExited) { throw "Renderer exited: $($process.ExitCode) $output $errors" }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Navigation test timed out. $output $errors"
}
finally {
    $env:DOTNATIVE_PREVIEW_PORT = $oldPort
    $env:DOTNATIVE_WINUI_NAVIGATION_TEST = $oldTest
    $env:DOTNATIVE_WINUI_WINDOW_TEST = $oldWindowTest
    if ($process -and !$process.HasExited) {
        # Only the renderer started above and its managed child.
        & taskkill.exe /PID $process.Id /T /F | Out-Null
    }
}
