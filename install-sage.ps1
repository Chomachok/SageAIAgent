param(
    [switch]$Force,
    [switch]$NoConfig,
    [string]$ConfigPath = ""
)

$ErrorActionPreference = "Stop"

Write-Host "🧙 Sage Installer v0.1.0" -ForegroundColor Cyan
Write-Host "==========================" -ForegroundColor Cyan

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "❌ .NET SDK not found. Please install .NET 8.0 SDK from https://dotnet.microsoft.com/download" -ForegroundColor Red
    exit 1
}
Write-Host "✅ .NET SDK found: $($dotnet.Source)" -ForegroundColor Green

$installed = dotnet tool list -g | Select-String "sage"
if ($installed) {
    Write-Host "🔍 Sage is already installed." -ForegroundColor Yellow
    if (-not $Force) {
        $response = Read-Host "Do you want to reinstall? (y/n)"
        if ($response -ne 'y') {
            Write-Host "❌ Installation cancelled." -ForegroundColor Red
            exit 0
        }
    }
    Write-Host "🗑️ Uninstalling old version..." -ForegroundColor Yellow
    dotnet tool uninstall --global Sage.CLI
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Failed to uninstall Sage." -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Old version removed." -ForegroundColor Green
}

Write-Host "🔨 Building Sage..." -ForegroundColor Yellow
$projectPath = Join-Path (Get-Location) "backend\src\Sage.CLI\Sage.CLI.csproj"
if (-not (Test-Path $projectPath)) {
    Write-Host "❌ Project file not found at: $projectPath" -ForegroundColor Red
    Write-Host "Please run this script from the root of the Sage repository." -ForegroundColor Yellow
    exit 1
}

$nupkgDir = Join-Path (Get-Location) "backend\src\Sage.CLI\nupkg"
if (Test-Path $nupkgDir) {
    Remove-Item -Path $nupkgDir -Recurse -Force
}
New-Item -ItemType Directory -Path $nupkgDir -Force | Out-Null

dotnet pack $projectPath -c Release -o $nupkgDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed." -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build successful." -ForegroundColor Green

Write-Host "📦 Installing Sage..." -ForegroundColor Yellow
$package = Get-ChildItem -Path $nupkgDir -Filter "*.nupkg" | Select-Object -First 1
if (-not $package) {
    Write-Host "❌ No .nupkg file found." -ForegroundColor Red
    exit 1
}
dotnet tool install --global --add-source $nupkgDir Sage.CLI
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Installation failed." -ForegroundColor Red
    exit 1
}
Write-Host "✅ Installation successful." -ForegroundColor Green

if (-not $NoConfig) {
    Write-Host "⚙️ Setting up global config..." -ForegroundColor Yellow
    
    $homeConfigDir = Join-Path $HOME ".sage"
    $homeConfigFile = Join-Path $homeConfigDir "config.json"
    
    if ($ConfigPath -and (Test-Path $ConfigPath)) {
        $sourceConfig = $ConfigPath
    }
    else {
        $sourceConfig = Join-Path (Get-Location) "sage.config.json"
    }
    
    if (Test-Path $sourceConfig) {
        Write-Host "✅ Found local config: $sourceConfig" -ForegroundColor Green
        if (-not (Test-Path $homeConfigDir)) {
            New-Item -ItemType Directory -Path $homeConfigDir -Force | Out-Null
        }
        Copy-Item -Path $sourceConfig -Destination $homeConfigFile -Force
        Write-Host "✅ Config copied to: $homeConfigFile" -ForegroundColor Green
    }
    else {
        Write-Host "⚠️ No config found. Creating template..." -ForegroundColor Yellow
        if (-not (Test-Path $homeConfigDir)) {
            New-Item -ItemType Directory -Path $homeConfigDir -Force | Out-Null
        }
        $template = @'
{
  "Provider": "Groq",
  "ModelId": "openai/gpt-oss-20b",
  "Endpoint": "https://api.groq.com/openai/v1",
  "ApiKey": "YOUR_API_KEY_HERE",
  "RootPath": ".",
  "MaxTokens": 500,
  "Temperature": 0.1,
  "MaxHistoryMessages": 3
}
'@
        $template | Out-File -FilePath $homeConfigFile -Encoding utf8
        Write-Host "✅ Template created at: $homeConfigFile" -ForegroundColor Green
        Write-Host "   Please edit this file and add your API key." -ForegroundColor Yellow
    }
}
else {
    Write-Host "⏭️ Skipping config setup (--NoConfig used)." -ForegroundColor Yellow
}

Write-Host "🧪 Testing installation..." -ForegroundColor Yellow
$testResult = sage --version 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Sage is ready to use!" -ForegroundColor Green
    Write-Host "   Run 'sage' to start interactive mode." -ForegroundColor White
}
else {
    Write-Host "⚠️ Sage installed but '--version' command failed. Try restarting your terminal." -ForegroundColor Yellow
}

Write-Host "🎉 Installation complete!" -ForegroundColor Cyan