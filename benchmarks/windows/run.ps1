param(
    [Parameter(Mandatory=$true)][ValidateSet('dotnative','maui')][string]$Framework,
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$Output,
    [Parameter(Mandatory=$true)][string]$Driver,
    [ValidateRange(1,10000)][int]$Iterations = 10,
    [ValidateRange(1,65535)][int]$PreviewPort = 14974,
    [switch]$VirtualMachine
)
$ErrorActionPreference = 'Stop'
# Use .NET 10 UI Automation rather than Windows PowerShell's legacy proxies.
& $Driver $Framework $Executable $Output $Iterations $PreviewPort ([bool]$VirtualMachine).ToString()
if ($LASTEXITCODE -ne 0) { throw "Benchmark failed ($LASTEXITCODE); inspect $Output" }
