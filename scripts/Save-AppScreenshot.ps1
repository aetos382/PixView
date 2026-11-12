#Requires -Version 7.0

<#
.SYNOPSIS
    実行中のアプリの画面を PNG 画像として保存する。

.DESCRIPTION
    Windows では、指定したプロセスのメイン ウィンドウだけを取得する。
    他のウィンドウに隠れていても取得でき、デスクトップの他の部分は写らない。
    Android では、adb につながっている端末またはエミュレーターの画面全体を取得する。

.EXAMPLE
    ./scripts/Save-AppScreenshot.ps1 -Platform Windows -ProcessName PixView.Prototype -OutFile shot.png

.EXAMPLE
    ./scripts/Save-AppScreenshot.ps1 -Platform Android -OutFile shot.png
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Windows', 'Android')]
    [string] $Platform,

    [Parameter(Mandatory)]
    [string] $OutFile,

    # Windows で取得対象とするプロセスの名前（拡張子なし）。
    [string] $ProcessName,

    # adb のパス。省略時は PATH と既定のインストール先から探す。
    [string] $AdbPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$OutFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)

function Save-WindowsScreenshot {
    if (-not $ProcessName) {
        throw 'Windows では -ProcessName の指定が必要です。'
    }

    # PC がロックされている間は、ウィンドウの内容が真っ白にしか取得できない。
    # 白い画像を「何も表示されていない」と誤解しないよう、取得せずにエラーにする。
    $sessionId = (Get-Process -Id $PID).SessionId
    if (Get-Process -Name LogonUI -ErrorAction Ignore | Where-Object { $_.SessionId -eq $sessionId }) {
        throw 'PC がロックされているため、ウィンドウの内容を取得できません。ロックを解除してから実行してください。'
    }

    $process = Get-Process -Name $ProcessName -ErrorAction Ignore |
        Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
        Select-Object -First 1
    if (-not $process) {
        throw "ウィンドウを持つプロセス '$ProcessName' が見つかりません。"
    }

    Add-Type -AssemblyName System.Drawing
    if (-not ('PixView.Tools.WindowCapture' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace PixView.Tools
{
    public static class WindowCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
    }
}
'@
    }

    # 高 DPI 環境でウィンドウの大きさを実ピクセルで得るために必要。
    [void][PixView.Tools.WindowCapture]::SetProcessDPIAware()

    $rect = [PixView.Tools.WindowCapture+Rect]::new()
    if (-not [PixView.Tools.WindowCapture]::GetWindowRect($process.MainWindowHandle, [ref] $rect)) {
        throw 'ウィンドウの位置を取得できません。'
    }

    $bitmap = [System.Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $deviceContext = $graphics.GetHdc()
            try {
                # 2 は PW_RENDERFULLCONTENT。WinUI のように合成で描画されるウィンドウの取得に必要。
                if (-not [PixView.Tools.WindowCapture]::PrintWindow($process.MainWindowHandle, $deviceContext, 2)) {
                    throw 'ウィンドウの内容を取得できません。'
                }
            }
            finally {
                $graphics.ReleaseHdc($deviceContext)
            }
        }
        finally {
            $graphics.Dispose()
        }

        $bitmap.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Resolve-Adb {
    $candidates = @(
        $AdbPath
        (Get-Command adb -ErrorAction Ignore)?.Source
        $env:ANDROID_HOME ? (Join-Path $env:ANDROID_HOME 'platform-tools/adb.exe') : $null
        $env:LOCALAPPDATA ? (Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe') : $null
        ${env:ProgramFiles(x86)} ? (Join-Path ${env:ProgramFiles(x86)} 'Android/android-sdk/platform-tools/adb.exe') : $null
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    throw 'adb が見つかりません。-AdbPath で場所を指定してください。'
}

function Save-AndroidScreenshot {
    $adb = Resolve-Adb
    $remote = '/sdcard/pixview-screenshot.png'

    # 標準出力経由ではバイナリが壊れることがあるので、端末側に保存してから取り出す。
    & $adb shell screencap -p $remote
    if ($LASTEXITCODE -ne 0) {
        throw '端末の画面を取得できません。'
    }

    & $adb pull $remote $OutFile | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw '画像を端末から取り出せません。'
    }

    & $adb shell rm $remote
}

switch ($Platform) {
    'Windows' { Save-WindowsScreenshot }
    'Android' { Save-AndroidScreenshot }
}

Get-Item -LiteralPath $OutFile
