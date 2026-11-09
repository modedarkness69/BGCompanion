# Сборка релиза: .\build.ps1  →  dist\BGCompanion-win-x64.zip
# Программа и апдейтер собираются в один файл каждый, .NET ставить пользователю не нужно.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'dist\BGCompanion'
$zip = Join-Path $root 'dist\BGCompanion-win-x64.zip'

if (Test-Path (Join-Path $root 'dist')) { Remove-Item (Join-Path $root 'dist') -Recurse -Force }

$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true', '-p:DebugType=none', '-o', $out)

dotnet publish (Join-Path $root 'src\BGCompanion\BGCompanion.csproj') @common
if ($LASTEXITCODE -ne 0) { throw 'Сборка BGCompanion не удалась' }
dotnet publish (Join-Path $root 'src\Updater\Updater.csproj') @common '-p:PublishTrimmed=true'
if ($LASTEXITCODE -ne 0) { throw 'Сборка Updater не удалась' }

Get-ChildItem $out -Filter *.pdb | Remove-Item
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force
Get-ChildItem $out | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
"Архив: $zip ({0:N1} МБ)" -f ((Get-Item $zip).Length / 1MB)
