$ErrorActionPreference = 'Stop'
$reviewRoot = 'C:\Users\User\AppData\Local\Temp\GameTest-AuthoredWorld-71defa6829c94a2f9ebe78d3a012cc0c'
$releaseRoot = Join-Path $reviewRoot 'CloudReleaseBuild'
$exportRoot = 'D:\GameTest3\GameTest\Builds\Windows64-20261009'
$archivePath = 'D:\GameTest3\GameTest\Builds\GameTest-Windows64-20261009.zip'
$evidenceRoot = $PSScriptRoot
if ((Test-Path -LiteralPath $exportRoot) -or (Test-Path -LiteralPath $archivePath)) {
    throw 'The destination already exists; preserve it and choose a new export name.'
}
$buildReport = Get-Content -LiteralPath (Join-Path $reviewRoot 'cloud-release-build-result.txt') -Raw
if (-not $buildReport.StartsWith('Succeeded') -or -not $buildReport.Contains('Validation instrumentation: False')) {
    throw 'A successful formal build is required.'
}
$cecilPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Data\MonoBleedingEdge\lib\mono\gac\Mono.Cecil\0.11.1.0__0738eb9f132ed756\Mono.Cecil.dll'
[void][Reflection.Assembly]::LoadFrom($cecilPath)
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $releaseRoot 'GameTest_Data\Managed\Assembly-CSharp.dll'))
try {
    $types = $assembly.MainModule.Types
    if ($types.Name -contains 'OverworldValidationProbe') { throw 'Validation probe leaked into the release.' }
    $coop = $types | Where-Object Name -eq 'CoopSession'
    if ($coop.Methods.Name -contains 'ReviewInternet') { throw 'Relay test hook leaked into the release.' }
    $friends = $types | Where-Object Name -eq 'CloudFriendsTransport'
    if (@($friends.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.Parameters.Count -gt 0 }).Count -gt 0) {
        throw 'Friends transport test injection leaked into the release.'
    }
    $world = $types | Where-Object Name -eq 'OverworldDirector'
    if ($world.Methods.Name -notcontains 'EnemyBoundaryAt') { throw 'New enemy territory implementation is absent.' }
} finally { $assembly.Dispose() }
New-Item -ItemType Directory -Path $exportRoot | Out-Null
Get-ChildItem -LiteralPath $releaseRoot | Where-Object Name -ne 'GameTest_BurstDebugInformation_DoNotShip' |
    Copy-Item -Destination $exportRoot -Recurse

function Get-Manifest([string]$directory) {
    $absoluteRoot = (Resolve-Path -LiteralPath $directory).Path.TrimEnd('\')
    @(Get-ChildItem -LiteralPath $absoluteRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{
            Path = $_.FullName.Substring($absoluteRoot.Length + 1)
            Bytes = $_.Length
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
}
$exportManifest = Get-Manifest $exportRoot
foreach ($entry in $exportManifest) {
    if ((Get-FileHash -LiteralPath (Join-Path $releaseRoot $entry.Path) -Algorithm SHA256).Hash -ne $entry.SHA256) {
        throw ('Export differs from formal build: ' + $entry.Path)
    }
}
$exportManifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'export-sha256-20261009.json') -Encoding UTF8
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ('GameTest-Export-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stageRoot | Out-Null
Copy-Item -LiteralPath $exportRoot -Destination (Join-Path $stageRoot 'GameTest') -Recurse
$readme = @'
GameTest — Windows 64 位元完整遊戲，2026-10-09

1. 請先將整個 ZIP 解壓縮。
2. 開啟 GameTest 資料夾，執行 GameTest.exe。
3. 請保留 GameTest_Data、MonoBleedingEdge、D3D12 與 DLL 等檔案的相對位置，不要只複製 EXE。

本版敵人偵測／追擊範圍由 100 提高至 200 Unity 單位；草原與森林魔物能在自己所屬區域內追擊。近戰與弩箭攻擊距離維持原設定。

可使用遊客模式進行單人、雙人分割畫面及區域網路連線；區域網路連線需要同一網路，主機防火牆須允許遊戲連線。
帳號與雲端好友／跨網路連線的用戶端程式已包含，但正式好友資料庫與公開 WSS 中繼服務仍待部署，這份輸出尚未提供正式外網多人服務。遊客無法使用好友功能。

本包是可執行的完整遊戲，不包含 Unity 編輯器或開發專案。
'@
[IO.File]::WriteAllText((Join-Path $stageRoot '使用說明.txt'), $readme, [Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stageRoot, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $false)
$extractRoot = Join-Path ([IO.Path]::GetTempPath()) ('GameTest-UnzipReview-' + [Guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extractRoot)
$unzippedGame = Join-Path $extractRoot 'GameTest'
$unzipManifest = Get-Manifest $unzippedGame
if ($unzipManifest.Count -ne $exportManifest.Count) { throw 'ZIP runtime file count differs.' }
$expected = @{}
foreach ($entry in $exportManifest) { $expected[$entry.Path] = $entry.SHA256 }
foreach ($entry in $unzipManifest) {
    if ($expected[$entry.Path] -ne $entry.SHA256) { throw ('ZIP differs: ' + $entry.Path) }
}
$startupLog = Join-Path $evidenceRoot 'export-startup-20261009.log'
$startup = Start-Process -FilePath (Join-Path $unzippedGame 'GameTest.exe') -ArgumentList @(
    '-batchmode', '-force-d3d11', '-screen-width', '1920', '-screen-height', '1080', '-logFile', $startupLog
) -WindowStyle Hidden -PassThru
$result = [pscustomobject]@{
    Result = 'Archive contents verified; normal release startup in progress'
    Export = $exportRoot
    Archive = $archivePath
    ArchiveSHA256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    ArchiveBytes = (Get-Item -LiteralPath $archivePath).Length
    RuntimeFiles = $exportManifest.Count
    RuntimeBytes = ($exportManifest | Measure-Object Bytes -Sum).Sum
    StartupPID = $startup.Id
    StartupExecutable = Join-Path $unzippedGame 'GameTest.exe'
    StartupLog = $startupLog
    StartupStartedUTC = [DateTime]::UtcNow.ToString('o')
    ReleaseInstrumentationExcluded = $true
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceRoot 'export-review-20261009.json') -Encoding UTF8
$result | ConvertTo-Json
