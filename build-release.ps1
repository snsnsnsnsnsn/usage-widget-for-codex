param(
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",

    [string]$OutputDirectory = "publish"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$project = Join-Path $PSScriptRoot "CodexUsageWidget.App\CodexUsageWidget.App.csproj"
$publish = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $PSScriptRoot $OutputDirectory
}
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"

if (Test-Path -LiteralPath $msbuild) {
    & $msbuild $project /restore /t:Publish /p:Configuration=Release "/p:RuntimeIdentifier=$Runtime" /p:SelfContained=true /p:PublishSingleFile=true "/p:PublishDir=$publish\" /m:1 /p:UseSharedCompilation=false /verbosity:minimal
} else {
    dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -o $publish
}

if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $publish "CodexUsageWidget.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Published executable was not created: $exe"
}

Get-Item -LiteralPath $exe | Select-Object FullName, Length, LastWriteTime
