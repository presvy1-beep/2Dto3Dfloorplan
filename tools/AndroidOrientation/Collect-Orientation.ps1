param(
    [string]$Serial,
    [string]$Adb = 'adb',
    [string]$Package = 'com.DefaultCompany.arn',
    [string]$Activity = 'com.arnav.filepicker.ArnavUnityActivity',
    [int]$StartupDelaySeconds = 8,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\Logs\orientation-capture')
)

$ErrorActionPreference = 'Stop'
if ($StartupDelaySeconds -lt 1) { throw 'StartupDelaySeconds must be positive.' }

$deviceLines = @(& $Adb devices)
if ($LASTEXITCODE -ne 0) { throw 'adb devices failed.' }
$connected = @($deviceLines | Where-Object { $_ -match '^([^\s]+)\s+device$' } | ForEach-Object { ($_ -split '\s+')[0] })
if (-not $Serial) {
    if ($connected.Count -ne 1) { throw "Expected one authorized device; found $($connected.Count). Use -Serial when several devices are connected." }
    $Serial = $connected[0]
}
if ($Serial -notin $connected) { throw "Device $Serial is not connected and authorized." }

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

function Invoke-Device([string[]]$Arguments) {
    $result = @(& $Adb -s $Serial @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "adb $($Arguments -join ' ') failed: $($result -join ' ')" }
    return $result
}

function Save-DeviceOutput([string]$Phase, [string]$Name, [string[]]$Arguments) {
    Invoke-Device $Arguments | Out-File -LiteralPath (Join-Path $OutputDirectory "$Phase-$Name.txt") -Encoding utf8
}

function Capture([string]$Phase) {
    $state = @(
        "phase=$Phase"
        "capturedUtc=$([DateTime]::UtcNow.ToString('o'))"
        "serial=$Serial"
        "model=$((Invoke-Device @('shell','getprop','ro.product.model')) -join ' ')"
        "androidRelease=$((Invoke-Device @('shell','getprop','ro.build.version.release')) -join ' ')"
        "androidSdk=$((Invoke-Device @('shell','getprop','ro.build.version.sdk')) -join ' ')"
        "physicalAndOverrideSize=$((Invoke-Device @('shell','wm','size')) -join ' ')"
        "density=$((Invoke-Device @('shell','wm','density')) -join ' ')"
        "accelerometer_rotation=$((Invoke-Device @('shell','settings','get','system','accelerometer_rotation')) -join ' ')"
        "user_rotation=$((Invoke-Device @('shell','settings','get','system','user_rotation')) -join ' ')"
    )
    $state | Out-File -LiteralPath (Join-Path $OutputDirectory "$Phase-state.txt") -Encoding utf8
    Save-DeviceOutput $Phase 'display' @('shell','dumpsys','display')
    Save-DeviceOutput $Phase 'window' @('shell','dumpsys','window')
    Save-DeviceOutput $Phase 'activity' @('shell','dumpsys','activity','activities')
    Save-DeviceOutput $Phase 'input' @('shell','dumpsys','input')
    Save-DeviceOutput $Phase 'package' @('shell','dumpsys','package',$Package)
    Save-DeviceOutput $Phase 'logcat' @('logcat','-d','-v','threadtime','-t','5000')
    Get-Content -LiteralPath (Join-Path $OutputDirectory "$Phase-logcat.txt") |
        Select-String -Pattern 'ARNav|Unity|ActivityTaskManager|WindowManager|DisplayManager|rotation|orientation' |
        Select-Object -Last 200 |
        Out-File -LiteralPath (Join-Path $OutputDirectory "$Phase-orientation-logcat.txt") -Encoding utf8
    $pattern = 'mCurrentFocus|mFocusedApp|mResumedActivity|topResumedActivity|rotation|orientation|DisplayInfo|logicalWidth|logicalHeight|Unity|ARNav'
    Get-ChildItem -LiteralPath $OutputDirectory -Filter "$Phase-*.txt" |
        Where-Object { $_.Name -ne "$Phase-logcat.txt" } |
        ForEach-Object { Get-Content -LiteralPath $_.FullName | Select-String -Pattern $pattern | Select-Object -First 100 } |
        Out-File -LiteralPath (Join-Path $OutputDirectory "$Phase-summary.txt") -Encoding utf8
    Write-Output "Captured $Phase orientation state."
}

# This script reads rotation settings; it never writes them.
Invoke-Device @('shell','am','force-stop',$Package) | Out-Null
Invoke-Device @('shell','input','keyevent','KEYCODE_HOME') | Out-Null
Start-Sleep -Seconds 2
Capture 'before'

Invoke-Device @('shell','am','start','-n',"$Package/$Activity") | Out-Null
Start-Sleep -Seconds $StartupDelaySeconds
Capture 'running'

Invoke-Device @('shell','input','keyevent','KEYCODE_HOME') | Out-Null
Start-Sleep -Seconds 2
Capture 'after'
Write-Output "Orientation capture saved to $OutputDirectory"
