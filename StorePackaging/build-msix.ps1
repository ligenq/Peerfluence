param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "1.0.0.0",
    [string]$PackageName = "Peerfluence",
    [string]$Publisher = "CN=Peerfluence",
    [string]$PublisherDisplayName = "Peerfluence",
    [string]$SignCertificatePath,
    [securestring]$SignCertificatePassword
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "Peerfluence\Peerfluence.csproj"
$templatePath = Join-Path $PSScriptRoot "Package.appxmanifest.template"
$iconPath = Join-Path $repoRoot "Peerfluence\Assets\application-icon.png"
$artifactsRoot = Join-Path $repoRoot "artifacts\store"
$publishDir = Join-Path $artifactsRoot "publish\$RuntimeIdentifier"
$stagingDir = Join-Path $artifactsRoot "msix-staging\$RuntimeIdentifier"
$packageDir = Join-Path $artifactsRoot "msix"
$packagePath = Join-Path $packageDir "Peerfluence-$Version-$RuntimeIdentifier.msix"

function Get-WindowsSdkTool {
    param([Parameter(Mandatory)][string]$ToolName)

    $kitRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    $tool = Get-ChildItem -Path $kitRoot -Recurse -Filter $ToolName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $tool) {
        throw "Could not find $ToolName under $kitRoot. Install the Windows SDK before building the MSIX package."
    }

    return $tool.FullName
}

function Convert-ToPackageArchitecture {
    param([Parameter(Mandatory)][string]$Rid)

    switch -Regex ($Rid) {
        "win-x64$" { return "x64" }
        "win-x86$" { return "x86" }
        "win-arm64$" { return "arm64" }
        default { throw "Unsupported RuntimeIdentifier '$Rid'. Use win-x64, win-x86, or win-arm64." }
    }
}

function Assert-PackageVersion {
    param([Parameter(Mandatory)][string]$PackageVersion)

    if ($PackageVersion -notmatch "^\d+\.\d+\.\d+\.0$") {
        throw "MSIX Store package versions must use four numeric parts and the fourth part must be 0, for example 1.0.0.0."
    }
}

function New-PackageImage {
    param(
        [Parameter(Mandatory)][System.Drawing.Image]$Source,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height
    )

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $scale = [Math]::Min($Width / $Source.Width, $Height / $Source.Height)
        $drawWidth = [int]($Source.Width * $scale)
        $drawHeight = [int]($Source.Height * $scale)
        $x = [int](($Width - $drawWidth) / 2)
        $y = [int](($Height - $drawHeight) / 2)
        $graphics.DrawImage($Source, $x, $y, $drawWidth, $drawHeight)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

Assert-PackageVersion $Version

$makeAppx = Get-WindowsSdkTool "makeappx.exe"
$architecture = Convert-ToPackageArchitecture $RuntimeIdentifier

Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publishDir, $stagingDir, $packageDir -Force | Out-Null

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDir `
    /p:DistributionChannel=MicrosoftStore `
    /p:UseSharedCompilation=false `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true

Copy-Item -Path (Join-Path $publishDir "*") -Destination $stagingDir -Recurse -Force
Get-ChildItem -LiteralPath $stagingDir -Recurse -Filter "*.pdb" | Remove-Item -Force

$assetsDir = Join-Path $stagingDir "Assets"
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

Add-Type -AssemblyName System.Drawing
$sourceImage = [System.Drawing.Image]::FromFile($iconPath)
try {
    New-PackageImage $sourceImage (Join-Path $assetsDir "Square44x44Logo.png") 44 44
    New-PackageImage $sourceImage (Join-Path $assetsDir "Square150x150Logo.png") 150 150
    New-PackageImage $sourceImage (Join-Path $assetsDir "Square310x310Logo.png") 310 310
    New-PackageImage $sourceImage (Join-Path $assetsDir "Wide310x150Logo.png") 310 150
    New-PackageImage $sourceImage (Join-Path $assetsDir "StoreLogo.png") 50 50
}
finally {
    $sourceImage.Dispose()
}

$manifest = Get-Content -LiteralPath $templatePath -Raw
$manifest = $manifest.Replace("{{PackageName}}", $PackageName)
$manifest = $manifest.Replace("{{Publisher}}", $Publisher)
$manifest = $manifest.Replace("{{PublisherDisplayName}}", $PublisherDisplayName)
$manifest = $manifest.Replace("{{Version}}", $Version)
$manifest = $manifest.Replace("{{Architecture}}", $architecture)
Set-Content -LiteralPath (Join-Path $stagingDir "AppxManifest.xml") -Value $manifest -Encoding UTF8

Remove-Item -LiteralPath $packagePath -Force -ErrorAction SilentlyContinue
& $makeAppx pack /v /o /d $stagingDir /p $packagePath

if ($LASTEXITCODE -ne 0) {
    throw "makeappx.exe failed with exit code $LASTEXITCODE."
}

if ($SignCertificatePath) {
    $signTool = Get-WindowsSdkTool "signtool.exe"
    $signArgs = @("sign", "/fd", "SHA256", "/f", $SignCertificatePath)

    if ($SignCertificatePassword) {
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SignCertificatePassword)
        try {
            $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
            $signArgs += @("/p", $plainPassword)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }

    $signArgs += $packagePath
    & $signTool @signArgs

    if ($LASTEXITCODE -ne 0) {
        throw "signtool.exe failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Created $packagePath"
