$ErrorActionPreference = 'Stop'
$petRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
Add-Type -AssemblyName System.Drawing
$atlas = [Drawing.Bitmap]::FromFile((Join-Path $petRoot 'assets\pet-atlas.png'))
$iconBitmap = New-Object Drawing.Bitmap 64,64
$graphics = [Drawing.Graphics]::FromImage($iconBitmap)
$graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.DrawImage($atlas, [Drawing.Rectangle]::new(8,0,48,64), [Drawing.Rectangle]::new(0,0,384,512), [Drawing.GraphicsUnit]::Pixel)
$icon = [Drawing.Icon]::FromHandle($iconBitmap.GetHicon())
$iconStream = [IO.File]::Create((Join-Path $petRoot 'assets\pet.ico'))
$icon.Save($iconStream)
$iconStream.Dispose()
$icon.Dispose()
$graphics.Dispose()
$iconBitmap.Dispose()
$atlas.Dispose()
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Xml.dll','System.Xml.Linq.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $wpf $_) }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ('/win32icon:' + (Join-Path $petRoot 'assets\pet.ico')) ('/out:' + (Join-Path $petRoot '启动或关闭桌宠.exe')) @references (Join-Path $PSScriptRoot 'Pet.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }

