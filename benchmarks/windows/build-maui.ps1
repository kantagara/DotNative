param(
    [Parameter(Mandatory=$true)][string]$Repository,
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [string]$Dotnet = 'dotnet',
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force "$WorkDirectory\Maui", "$WorkDirectory\Shared" | Out-Null
# Copy only sources; never reuse another OS's NuGet/obj graph.
Copy-Item "$Repository\benchmarks\Maui\MauiBenchmark.csproj", "$Repository\benchmarks\Maui\App.cs" "$WorkDirectory\Maui" -Force
Copy-Item "$Repository\benchmarks\Maui\Platforms" "$WorkDirectory\Maui" -Recurse -Force
Copy-Item "$Repository\benchmarks\Shared\*.cs" "$WorkDirectory\Shared" -Force
& $Dotnet publish "$WorkDirectory\Maui\MauiBenchmark.csproj" -c Release -f net10.0-windows10.0.19041.0 -r $Runtime --self-contained true -p:WindowsAppSDKSelfContained=true -o "$WorkDirectory\publish-$Runtime"
if ($LASTEXITCODE -ne 0) { throw "MAUI publish failed ($LASTEXITCODE)" }
