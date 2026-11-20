$ErrorActionPreference='Stop'
$setup=Get-Content (Join-Path $PSScriptRoot 'Measure-Reply-Overlay.ps1') -Raw
$setup=$setup.Replace("`r`n","`n")
$setup=$setup.Substring(0,$setup.IndexOf("try {`n `$grid.UpdateLayout()"))
$fixtureRoot=$PSScriptRoot;$setup=$setup.Replace('$PSScriptRoot','$fixtureRoot')
Invoke-Expression $setup
try {
 Check ($assembly.GetName().Version -ge [version]'0.20.4') 'Canonical executable predates rounded-corner update'
 [void](Invoke $window ApplySimulationScale @(.45));$grid.UpdateLayout()
 $scene=Field $window scene
 $seedCapture=New-Object Windows.Media.Imaging.RenderTargetBitmap ([int]$grid.ActualWidth),([int]$grid.ActualHeight),96,96,([Windows.Media.PixelFormats]::Pbgra32)
 $seedCapture.Render($grid)
 $effects=Field $scene smoke
 $gpu=Field $effects gpuInk
 Check ($null -ne $gpu -and !(Prop $gpu Failed)) 'Active native smoke GPU missing'
 Invoke $gpu SeedAmbient
 foreach($i in 1..6){[void](Invoke $gpu Advance)}
 $image=Prop $gpu Image
 Check ($image -is [Windows.Interop.D3DImage]) 'Smoke does not use a native GPU surface'
 $copy=$image.GetType().GetMethod('CopyBackBuffer',$flags).Invoke($image,$null)
 Check ($null -ne $copy) 'Native GPU surface readback unavailable'
 $gpuPixels=[byte[]]::new($copy.PixelWidth*$copy.PixelHeight*4)
 $copy.CopyPixels($gpuPixels,$copy.PixelWidth*4,0)
 $unique=New-Object 'Collections.Generic.HashSet[int]'
 for($i=0;$i -lt $gpuPixels.Length;$i+=400){[void]$unique.Add(([int]$gpuPixels[$i] -shl 16) -bor ([int]$gpuPixels[$i+1] -shl 8) -bor [int]$gpuPixels[$i+2])}
 Check ($unique.Count -gt 10) 'GPU image is blank or flat; gradient-only fixture is insufficient'
 # Draw the active GPU surface directly under the actual inset canvas clip.
 $layout=Field $window layout
 $live=New-Object Windows.Controls.Image
 $live.Source=$image;$live.Stretch=[Windows.Media.Stretch]::Fill
 [void]$layout.Children.Add($live);$grid.UpdateLayout()
 $capture=New-Object Windows.Media.Imaging.RenderTargetBitmap ([int]$grid.ActualWidth),([int]$grid.ActualHeight),96,96,([Windows.Media.PixelFormats]::Pbgra32)
 $capture.Render($grid)
 $pixels=[byte[]]::new($capture.PixelWidth*$capture.PixelHeight*4);$capture.CopyPixels($pixels,$capture.PixelWidth*4,0)
 $origin=$layout.TransformToAncestor($grid).Transform([Windows.Point]::new(0,0))
 foreach($p in @(@(2,2),@(($layout.ActualWidth-3),2),@(2,($layout.ActualHeight-3)),@(($layout.ActualWidth-3),($layout.ActualHeight-3)))) {
  $index=4*(([int]($origin.Y+$p[1]))*$capture.PixelWidth+[int]($origin.X+$p[0]))
  Check ($pixels[$index] -eq 0 -and $pixels[$index+1] -eq 0 -and $pixels[$index+2] -eq 0) 'Live GPU surface leaks through rounded corners'
 }
 $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($capture))
 $file=[IO.File]::Create((Join-Path $root 'ChatCraft/Bleeding-Edge/Validation/Live-GPU-Rounded-0.20.4.png'))
 try{$encoder.Save($file)}finally{$file.Dispose()}
 "PASS: canonical $($assembly.GetName().Version), active native GPU readback with $($unique.Count) sampled colors, four rounded-corner pixel masks. Offscreen/no audio/chat."
}finally{$hwnd.Dispose();$window.Close()}
