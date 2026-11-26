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
# Inspect metadata without loading mount code or resolving engine dependencies.
# File presence alone also accepts stale packages from before the runtime extraction.
Add-Type -AssemblyName System.Reflection.Metadata
$assemblyPath = Join-Path $PackageRoot 'package.local.dieselengine.dll'
$stream = [IO.File]::OpenRead($assemblyPath)
$pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $types = @{}
    foreach ($handle in $metadata.TypeDefinitions) {
        $definition = $metadata.GetTypeDefinition($handle)
        $name = $metadata.GetString($definition.Namespace) + '.' + $metadata.GetString($definition.Name)
        $types[$name] = $definition
    }
    $required = @(
        'Diesel.PaydayUnitState', 'Diesel.PaydayMapState', 'Diesel.PaydayDoorPlacement',
        'Diesel.PaydayScriptedProp', 'Diesel.PaydayAnimationInfo', 'Diesel.PaydayUnitAnimationInfo'
    )
    foreach ($name in $required) {
        if (!$types.ContainsKey($name)) { throw "Stale or incomplete mount assembly: missing $name" }
    }
    foreach ($name in @('Diesel.PaydayMission', 'Diesel.PaydayMissionOrder', 'Diesel.PaydayMissionSpawn')) {
        if ($types.ContainsKey($name)) { throw "Archived mission runtime is still compiled: $name" }
    }
    $methods = @{
        'Diesel.PaydayMount' = @('GetAnimationInfo', 'GetUnitAnimationInfo')
        'Diesel.PaydayScriptedProp' = @('get_AnimationGroup')
        'Diesel.PaydayUnitState' = @('get_DamageBodies')
        'Diesel.DieselModelLoader' = @('ResolveChild')
    }
    foreach ($name in $methods.Keys) {
        if (!$types.ContainsKey($name)) { throw "Missing packaged type: $name" }
        $available = @($types[$name].GetMethods() | ForEach-Object {
            $metadata.GetString($metadata.GetMethodDefinition($_).Name)
        })
        foreach ($method in $methods[$name]) {
            if ($method -notin $available) { throw "Stale packaged API: $name.$method is missing" }
        }
    }
} finally {
    $pe.Dispose()
    $stream.Dispose()
}
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
    RuntimeApi = 'PASS (unit/map state, animation resources, metadata, scripted group playback)'
    ArchivedMissionRuntime = 'ABSENT'
    AssemblySha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $assemblyPath).Hash
    CompiledShaders = $shaders.Count
    CompiledMapThumbnails = $levels.Count
    Result = 'PASS (artifact completeness; client rendering requires a separate check)'
}
