param([string]$OutputPath = "$PSScriptRoot\..\KeyOverlay.exe")
$ErrorActionPreference='Stop'
$wpf="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
$compiler="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /codepage:65001 /target:winexe "/out:$OutputPath" "/win32icon:$PSScriptRoot\KeyOverlay.ico" "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$PSScriptRoot\KeyOverlay.cs" "$PSScriptRoot\UpstreamKeys.cs" "$PSScriptRoot\OverlayBehavior.cs"
if($LASTEXITCODE -ne 0){throw 'Build failed'}
