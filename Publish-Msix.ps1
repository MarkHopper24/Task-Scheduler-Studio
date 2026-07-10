[CmdletBinding()]
param(
    [switch] $SkipLocalSigning
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'Tasker.App\Tasker.App.csproj'
$manifestPath = Join-Path $root 'Tasker.App\Package.appxmanifest'
$dist = Join-Path $root 'dist'
$packageRoot = Join-Path $root 'Tasker.App\AppPackages\Release'

[xml] $manifest = Get-Content $manifestPath
$version = $manifest.Package.Identity.Version

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "Visual Studio's vswhere.exe was not found at '$vswhere'."
}

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    throw 'MSBuild was not found. Install Visual Studio Build Tools with MSBuild support.'
}

$sdkBin = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Recurse -Filter MakeAppx.exe |
    Where-Object { $_.FullName -match '\\x64\\MakeAppx\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty DirectoryName
if (-not $sdkBin) {
    throw 'MakeAppx.exe was not found. Install the Windows SDK.'
}

$makeAppx = Join-Path $sdkBin 'MakeAppx.exe'
$signTool = Join-Path $sdkBin 'SignTool.exe'
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "TaskSchedulerStudio-$version-$([guid]::NewGuid())"

try {
    Remove-Item $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $packageRoot, $staging, $dist | Out-Null

    foreach ($architecture in 'x64', 'ARM64') {
        $runtimeIdentifier = "win-$($architecture.ToLowerInvariant())"
        $architectureOutput = Join-Path $packageRoot $architecture
        New-Item -ItemType Directory -Force -Path $architectureOutput | Out-Null

        & $msbuild $project /nologo /restore `
            "/p:Configuration=Release" `
            "/p:Platform=$architecture" `
            "/p:RuntimeIdentifier=$runtimeIdentifier" `
            '/p:GenerateAppxPackageOnBuild=true' `
            '/p:UapAppxPackageBuildMode=SideloadOnly' `
            '/p:AppxBundle=Never' `
            '/p:AppxSymbolPackageEnabled=false' `
            '/p:AppxPackageSigningEnabled=false' `
            "/p:AppxPackageDir=$architectureOutput\"
        if ($LASTEXITCODE -ne 0) {
            throw "MSIX build failed for $architecture."
        }

        $component = Get-ChildItem $architectureOutput -Recurse -Filter "*.msix" |
            Where-Object { $_.Name -eq "Tasker.App_${version}_$($architecture.ToLowerInvariant()).msix" } |
            Select-Object -First 1
        if (-not $component) {
            throw "The $architecture component package was not produced."
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($component.FullName)
        try {
            $requiredMcpFiles = 'mcp/Tasker.Mcp.exe', 'mcp/Tasker.Mcp.dll', 'mcp/Tasker.Mcp.deps.json', 'mcp/Tasker.Mcp.runtimeconfig.json'
            $entries = [System.Collections.Generic.HashSet[string]]::new(
                [string[]] @($archive.Entries | ForEach-Object FullName),
                [System.StringComparer]::OrdinalIgnoreCase)
            $missingFiles = @($requiredMcpFiles | Where-Object { -not $entries.Contains($_) })
            if ($missingFiles) {
                throw "The $architecture component package is missing MCP runtime files: $($missingFiles -join ', ')."
            }
        }
        finally {
            $archive.Dispose()
        }

        Copy-Item $component.FullName (Join-Path $staging $component.Name)
    }

    $storeBundle = Join-Path $dist "TaskSchedulerStudio_${version}_x64_arm64_STOREUPLOAD.msixbundle"
    $signedBundle = Join-Path $dist "TaskSchedulerStudio_${version}_x64_arm64_SIGNED.msixbundle"
    Remove-Item $storeBundle, $signedBundle -Force -ErrorAction SilentlyContinue

    & $makeAppx bundle /d $staging /p $storeBundle /o
    if ($LASTEXITCODE -ne 0) {
        throw 'Creating the Store bundle failed.'
    }

    if (-not $SkipLocalSigning) {
        $certificate = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object {
                $_.HasPrivateKey -and
                $_.Subject -eq $manifest.Package.Identity.Publisher
            } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
        if (-not $certificate) {
            throw "No local signing certificate with private key was found for '$($manifest.Package.Identity.Publisher)'."
        }

        Copy-Item $storeBundle $signedBundle
        & $signTool sign /fd SHA256 /s My /sha1 $certificate.Thumbprint $signedBundle
        if ($LASTEXITCODE -ne 0) {
            throw 'Signing the local bundle failed.'
        }
    }

    Write-Host "Store upload bundle: $storeBundle"
    if (-not $SkipLocalSigning) {
        Write-Host "Signed local bundle: $signedBundle"
    }
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
