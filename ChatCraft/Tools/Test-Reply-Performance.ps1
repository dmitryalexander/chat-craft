$ErrorActionPreference='Stop'
# Reuse the owned, offscreen, audio-disabled fixture setup from the measurement tool.
$setup=Get-Content (Join-Path $PSScriptRoot 'Measure-Reply-Overlay.ps1') -Raw
$setup=$setup.Replace("`r`n","`n")
$setup=$setup.Substring(0,$setup.IndexOf("try {`n `$grid.UpdateLayout()"))
$fixtureRoot=$PSScriptRoot
$setup=$setup.Replace('$PSScriptRoot','$fixtureRoot')
Invoke-Expression $setup
Add-Type -ReferencedAssemblies System.Drawing @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class OverlayPixels {
 public static byte[] Read(Bitmap b) {
  var bits=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
  try{var a=new byte[bits.Stride*b.Height];Marshal.Copy(bits.Scan0,a,0,a.Length);return a;}finally{b.UnlockBits(bits);}
 }
 public static void Check(byte[] full,byte[] cropped,int width,int height,int top,int rows) {
  int stride=width*4;
  for(int y=0;y<height;y++)for(int x=0;x<stride;x++) {
   byte actual=y>=top&&y<top+rows?cropped[(y-top)*stride+x]:(byte)0;
   if(full[y*stride+x]!=actual)throw new Exception("Cropped overlay differs from full frame at row "+y);
  }
 }
 public static void Equal(byte[] a,byte[] b) {
  if(a.Length!=b.Length)throw new Exception("Frame sizes differ");
  for(int i=0;i<a.Length;i++)if(a[i]!=b[i])throw new Exception("Partial clear differs from full clear");
 }
}
'@
try {
 $grid.UpdateLayout()
 $text=('The branching clouds, typography, punctuation! '+[char]0x65e5+[char]0x672c+[char]0x8a9e+' ')*60
 Invoke $window OpenReplyText @($text)
 $flow=Field $window flow;$reader=Prop $flow Reader
 $reader.PausePlayback();(Field $window timer).Stop()
 $overlay=Field $window replyOverlay;$scene=Field $window scene;$smoke=Field $scene smoke
 $grid.UpdateLayout()
 for($i=0;$i -lt 40;$i++) {
  [void]$reader.Step(1);Invoke $overlay UpdateFrame
  $raster=Field $reader raster;$frame=Prop $raster Frame
  $full=[OverlayPixels]::Read($frame)
  $band=Field $overlay uploadBounds;$image=Field $overlay image
  $pixels=[byte[]]::new($image.PixelWidth*$image.PixelHeight*4)
  $image.CopyPixels($pixels,$image.PixelWidth*4,0)
  [OverlayPixels]::Check($full,$pixels,$frame.Width,$frame.Height,$band.Top,$band.Height)
  $raster.GetType().GetField('clearPixel',$flags).SetValue($raster,[uint32]::MaxValue)
  $reader.GetType().GetField('rasterDirty',$flags).SetValue($reader,$true)
  Invoke $overlay UpdateFrame
  [OverlayPixels]::Equal($full,[OverlayPixels]::Read((Prop $raster Frame)))
 }
 if((Prop $smoke MinimumInkFrameMilliseconds) -ne 33){throw 'Reply GPU interval not capped'}
 if((Field $smoke timer).Interval.TotalMilliseconds -lt 33){throw 'Smoke timer bypassed budget'}
 Invoke $window ShowDraft
 if((Prop $smoke MinimumInkFrameMilliseconds) -ne 0){throw 'Typing retained reply budget'}
 Invoke $scene BeginThinking
 if((Prop $smoke MinimumInkFrameMilliseconds) -ne 0){throw 'Thinking retained reply budget'}
 'PASS: 40 frames: cropped upload pixel-identical to full overlay, partial clear pixel-identical to full clear, reply33ms budget and Typing/Thinking reset. No live chats or messages.'
}finally{$hwnd.Dispose();$window.Close()}
