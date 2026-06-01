param(
    [Parameter(Mandatory)]
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$PackId = "Peerfluence.Desktop",
    [string]$PackTitle = "Peerfluence",
    [string]$PackAuthors = "ligenq",
    [string]$Channel = "win",
    [string]$ReleaseNotes,
    [switch]$CleanReleaseOutput,
    [switch]$CreateTestCertificate,
    [switch]$TrustTestCertificate,
    [string]$TestCertificateSubject = "CN=ligenq",
    [string]$TestCertificatePath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "artifacts\velopack\certs\Peerfluence-TestSigning.pfx"),
    [switch]$Msi,
    [string]$SignParams,
    [string]$SignTemplate
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "Peerfluence\Peerfluence.csproj"
$iconPath = Join-Path $repoRoot "Peerfluence\Assets\application-icon.ico"
$artifactsRoot = Join-Path $repoRoot "artifacts\velopack"
$publishDir = Join-Path $artifactsRoot "publish\$RuntimeIdentifier"
$releaseDir = Join-Path $artifactsRoot "releases\$RuntimeIdentifier"

function Assert-Version {
    param([Parameter(Mandatory)][string]$Value)

    if ($Value -notmatch "^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$") {
        throw "Version '$Value' is not valid for Velopack. Use SemVer like 1.0.0 or 1.0.0-beta.1."
    }
}

function Get-WindowsSdkTool {
    param([Parameter(Mandatory)][string]$ToolName)

    $kitRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    $tool = Get-ChildItem -Path $kitRoot -Recurse -Filter $ToolName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $tool) {
        throw "Could not find $ToolName under $kitRoot. Install the Windows SDK or pass your own -SignTemplate."
    }

    return $tool.FullName
}

function New-TestSigningCertificate {
    param(
        [Parameter(Mandatory)][string]$Subject,
        [Parameter(Mandatory)][string]$OutputPath
    )

    if (-not (Get-Command New-SelfSignedCertificate -ErrorAction SilentlyContinue)) {
        throw "New-SelfSignedCertificate is unavailable. Create a code-signing certificate manually and pass signing options with -SignParams or -SignTemplate."
    }

    $outputDirectory = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Subject `
        -KeyUsage DigitalSignature `
        -FriendlyName "Peerfluence Velopack Test Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

    $password = ConvertTo-SecureString -String "peerfluence-test" -Force -AsPlainText
    $certificatePath = [System.IO.Path]::ChangeExtension($OutputPath, ".cer")
    Export-PfxCertificate -Cert $certificate -FilePath $OutputPath -Password $password | Out-Null
    Export-Certificate -Cert $certificate -FilePath $certificatePath | Out-Null

    return @{
        CertificatePath = $certificatePath
        Path = $OutputPath
        Password = $password
        Thumbprint = $certificate.Thumbprint
    }
}

Assert-Version $Version

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "Could not find 'vpk' on PATH. Install the Velopack CLI with: dotnet tool install -g vpk"
}

if ($CreateTestCertificate) {
    if ($SignParams -or $SignTemplate) {
        throw "Use either -CreateTestCertificate or explicit signing parameters, not both."
    }

    $testCertificate = New-TestSigningCertificate -Subject $TestCertificateSubject -OutputPath $TestCertificatePath
    $signtool = Get-WindowsSdkTool "signtool.exe"
    $SignTemplate = "`"$signtool`" sign /fd SHA256 /sha1 $($testCertificate.Thumbprint) /s My /tr http://timestamp.digicert.com /td SHA256 `"{{file}}`""

    Write-Host "Created test signing certificate $($testCertificate.Path)"
    Write-Host "Created public certificate $($testCertificate.CertificatePath)"
    Write-Host "Certificate subject: $TestCertificateSubject"
    Write-Host "Certificate thumbprint: $($testCertificate.Thumbprint)"
    Write-Host "Test certificate PFX password: peerfluence-test"

    if ($TrustTestCertificate) {
        Import-Certificate -FilePath $testCertificate.CertificatePath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
        certutil -user -addstore Root $testCertificate.CertificatePath | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to import public certificate into Current User > Trusted Root Certification Authorities."
        }

        Write-Host "Imported public certificate into Current User > Trusted People and Trusted Root Certification Authorities."
    }
    else {
        Write-Host "For local install, import the public certificate into Current User > Trusted People and Trusted Root Certification Authorities."
    }
}

Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
if ($CleanReleaseOutput) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Path $publishDir, $releaseDir -Force | Out-Null

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDir `
    /p:UseSharedCompilation=false `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true

$packArgs = @(
    "pack",
    "--packId", $PackId,
    "--packVersion", $Version,
    "--packDir", $publishDir,
    "--mainExe", "Peerfluence.exe",
    "--outputDir", $releaseDir,
    "--channel", $Channel,
    "--runtime", $RuntimeIdentifier,
    "--packTitle", $PackTitle,
    "--packAuthors", $PackAuthors,
    "--icon", $iconPath
)

if ($ReleaseNotes) {
    $resolvedReleaseNotes = Resolve-Path $ReleaseNotes
    $packArgs += @("--releaseNotes", $resolvedReleaseNotes)
}

if ($Msi) {
    $packArgs += "--msi"
}

if ($SignParams) {
    $packArgs += @("--signParams", $SignParams)
}

if ($SignTemplate) {
    $packArgs += @("--signTemplate", $SignTemplate)
}

& vpk @packArgs

if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed with exit code $LASTEXITCODE."
}

Write-Host "Created Velopack release in $releaseDir"
Get-ChildItem -LiteralPath $releaseDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
