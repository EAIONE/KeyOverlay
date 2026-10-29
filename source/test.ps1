$ErrorActionPreference='Stop'
$compiler="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
& $compiler /nologo "/out:$PSScriptRoot\BehaviorTests.exe" /reference:System.Windows.Forms.dll "$PSScriptRoot\BehaviorTests.cs" "$PSScriptRoot\OverlayBehavior.cs"
if($LASTEXITCODE -ne 0){throw 'Behavior test compilation failed'}
& "$PSScriptRoot\BehaviorTests.exe"
if($LASTEXITCODE -ne 0){throw 'Behavior tests failed'}
& $compiler /nologo /target:exe /main:RecordingTests "/out:$PSScriptRoot\RecordingTests.exe" "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$PSScriptRoot\RecordingTests.cs" "$PSScriptRoot\KeyOverlay.cs" "$PSScriptRoot\OverlayBehavior.cs" "$PSScriptRoot\UpstreamKeys.cs"
if($LASTEXITCODE -ne 0){throw 'Recording test compilation failed'}
& "$PSScriptRoot\RecordingTests.exe"
if($LASTEXITCODE -ne 0){throw 'Recording tests failed'}
