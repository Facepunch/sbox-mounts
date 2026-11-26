param(
    [string] $EngineRoot = $env:FACEPUNCH_ENGINE,
    [string] $PackageRoot
)

$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
if (!$PackageRoot) {
    if (!$EngineRoot) { throw 'Supply -EngineRoot or -PackageRoot (or set FACEPUNCH_ENGINE).' }
    $PackageRoot = Join-Path $EngineRoot 'mounts/DieselEngine'
}

function Require-File([string] $Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -eq 0) {
        throw "Missing or empty packaged asset: $Path"
    }
}

Require-File (Join-Path $PackageRoot 'package.local.dieselengine.dll')
$shaders = @(Get-ChildItem (Join-Path $project 'Assets') -Recurse -Filter '*.shader')
foreach ($shader in $shaders) {
    $relative = [IO.Path]::GetRelativePath($project, $shader.FullName) + '_c'
    Require-File (Join-Path $PackageRoot $relative)
}
if ($shaders.Count -eq 0) { throw 'No source shaders found; check the project path.' }

$levels = @('bank', 'street', 'bridge', 'apartment', 'slaughterhouse', 'diamondheist', 'suburbia', 'secret_stash', 'l4d')
foreach ($level in $levels) {
    $resource = "levels/$level/world.world.scene"
    Require-File (Join-Path $project "Mounts/payday/thumbs/$resource.png")
    # Matches the engine BuildMount.ThumbHash / ThumbLoader resource-path lookup.
    $bytes = [Text.Encoding]::UTF8.GetBytes($resource.ToLowerInvariant())
    $hash = [Convert]::ToHexString([Security.Cryptography.MD5]::HashData($bytes)).Substring(0, 16).ToLowerInvariant()
    Require-File (Join-Path $PackageRoot "Mounts/payday/thumbs/$hash.vtex_c")
}

[pscustomobject]@{
    Package = [IO.Path]::GetFullPath($PackageRoot)
    Assembly = 'PASS'
    CompiledShaders = $shaders.Count
    CompiledMapThumbnails = $levels.Count
    Result = 'PASS (artifact completeness; client rendering requires a separate check)'
}
