param([Parameter(Mandatory=$true)][string]$AppDirectory,
      [Parameter(Mandatory=$true)][string]$OutputDirectory,
      [switch]$VerifyFade)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Remove-Item (Join-Path $OutputDirectory 'PASS.txt'),(Join-Path $OutputDirectory 'FAIL.txt') -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class NavigationProbe {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left,top,right,bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx,dy; public uint data,flags,time; public UIntPtr extra; }
 [StructLayout(LayoutKind.Explicit,Size=40)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mouse; }
 public delegate bool EnumProc(IntPtr hwnd,IntPtr param);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr param);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumProc callback,IntPtr param);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,UIntPtr count,StringBuilder text,uint flags,uint timeout,out UIntPtr result);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
 [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] inputs,int size);
 [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
 public static string Text(IntPtr h) {var s=new StringBuilder(512);UIntPtr result;SendMessageTimeout(h,13,(UIntPtr)s.Capacity,s,2,200,out result);return s.ToString();}
 public static IntPtr Window(int pid) {IntPtr found=IntPtr.Zero;EnumWindows((h,p)=>{uint id;GetWindowThreadProcessId(h,out id);if(id==pid && Text(h).StartsWith("DotNative Preview")){found=h;return false;}return true;},IntPtr.Zero);return found;}
 public static IntPtr Find(IntPtr window,string text) {IntPtr found=IntPtr.Zero;EnumChildWindows(window,(h,p)=>{if(Text(h)==text){found=h;return false;}return true;},IntPtr.Zero);return found;}
 public static string Dump(IntPtr window) {var text=new StringBuilder();EnumChildWindows(window,(h,p)=>{var label=Text(h);if(label.Length>0)text.AppendLine(h+" visible="+IsWindowVisible(h)+" enabled="+IsWindowEnabled(h)+" text="+label);return true;},IntPtr.Zero);return text.ToString();}
 public static bool Usable(IntPtr control,IntPtr root) {for(var h=control;h!=IntPtr.Zero;h=GetParent(h)){if(!IsWindowVisible(h)||!IsWindowEnabled(h))return false;if(h==root)return true;}return false;}
 public static bool Checked(IntPtr control) {return SendMessage(control,0x00F0,IntPtr.Zero,IntPtr.Zero).ToInt64()==1;}
 public static void Fit(IntPtr window) {SetWindowPos(window,IntPtr.Zero,20,20,Math.Min(900,GetSystemMetrics(0)-40),Math.Min(760,GetSystemMetrics(1)-80),0x0040);}
 public static void Click(IntPtr control) {RECT r;GetWindowRect(control,out r);int x=(r.left+r.right)/2,y=(r.top+r.bottom)/2;Mouse(x,y,2);Mouse(x,y,4);}
 static void Mouse(int x,int y,uint flags) {var i=new INPUT();i.mouse.dx=(int)((long)x*65535/(GetSystemMetrics(0)-1));i.mouse.dy=(int)((long)y*65535/(GetSystemMetrics(1)-1));i.mouse.flags=flags|0x8000|1;if(SendInput(1,new[]{i},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Exception("SendInput failed");}
}
'@
[NavigationProbe]::SetProcessDPIAware() | Out-Null
function Await([string]$reason,[scriptblock]$check,[int]$timeout=10) {
    $deadline=[DateTime]::UtcNow.AddSeconds($timeout)
    do { if(& $check){return}; Start-Sleep -Milliseconds 5 } while([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $reason"
}
function Capture([IntPtr]$window,[string]$name) {
    $r=New-Object NavigationProbe+RECT
    [NavigationProbe]::GetWindowRect($window,[ref]$r)|Out-Null
    $image=New-Object Drawing.Bitmap ($r.right-$r.left),($r.bottom-$r.top)
    $graphics=[Drawing.Graphics]::FromImage($image)
    $graphics.CopyFromScreen($r.left,$r.top,0,0,$image.Size)
    $image.Save((Join-Path $OutputDirectory $name))
    $graphics.Dispose();$image.Dispose()
}
function Assert-NoBlackBand([string]$path) {
    $image=[Drawing.Bitmap]::FromFile($path)
    try {
        for($y=35;$y -lt $image.Height-20;$y+=2) {
            $longest=0;$run=0
            for($x=8;$x -lt $image.Width-8;$x+=2) {
                $pixel=$image.GetPixel($x,$y)
                if($pixel.R -lt 12 -and $pixel.G -lt 12 -and $pixel.B -lt 12) {
                    $run++; if($run -gt $longest){$longest=$run}
                } else {$run=0}
            }
            if($longest -gt (($image.Width-16)/2)*0.65) {
                throw "Black compositor band in $path at y=$y"
            }
        }
    } finally {$image.Dispose()}
}
$preview=$null
$window=[IntPtr]::Zero
try {
    $smoke=Start-Process (Join-Path $AppDirectory 'WidgetSamples.exe') -ArgumentList '--smoke' -WorkingDirectory $AppDirectory -RedirectStandardOutput (Join-Path $OutputDirectory 'native-smoke.log') -RedirectStandardError (Join-Path $OutputDirectory 'native-smoke-errors.log') -PassThru
    $smokeHandle=$smoke.Handle
    if(!$smoke.WaitForExit(30000)){$smoke.Kill();throw 'Native navigation smoke timed out'}
    if($smoke.ExitCode -ne 0){throw "Native navigation smoke failed: $($smoke.ExitCode)"}
    $preview=Start-Process (Join-Path $AppDirectory 'WidgetSamples.exe') -WorkingDirectory $AppDirectory -RedirectStandardOutput (Join-Path $OutputDirectory 'preview.log') -RedirectStandardError (Join-Path $OutputDirectory 'preview-errors.log') -PassThru
    Await 'preview window' { [NavigationProbe]::Window($preview.Id) -ne [IntPtr]::Zero } 30
    $window=[NavigationProbe]::Window($preview.Id)
    [NavigationProbe]::SetForegroundWindow($window)|Out-Null
    [NavigationProbe]::Fit($window)
    Await 'Cart action' { [NavigationProbe]::Find($window,'Cart') -ne [IntPtr]::Zero } 30
    Start-Sleep -Milliseconds 300
    Capture $window 'home-before.png'
    $durations=@()
    for($cycle=0;$cycle -lt 5;$cycle++) {
        $cart=[NavigationProbe]::Find($window,'Cart')
        $started=[Diagnostics.Stopwatch]::StartNew()
        [NavigationProbe]::Click($cart)
        if($cycle -eq 0) {
            foreach($at in 25,50,75,100,125,150,175,200,225,250,275,300,325,350,375) {
                $wait=$at-$started.ElapsedMilliseconds
                if($wait -gt 0){Start-Sleep -Milliseconds $wait}
                Capture $window ("push-{0:D3}.png" -f $at)
            }
        }
        Await 'push completion' {
            $back=[NavigationProbe]::Find($window,'Back')
            $back -ne [IntPtr]::Zero -and [NavigationProbe]::Usable($back,$window)
        }
        if($cycle -eq 0){Capture $window 'push-final.png'}
        $durations += $started.ElapsedMilliseconds
        $back=[NavigationProbe]::Find($window,'Back')
        $started.Restart(); [NavigationProbe]::Click($back)
        Await 'Back completion' {
            $next=[NavigationProbe]::Find($window,'Cart')
            $next -ne [IntPtr]::Zero -and [NavigationProbe]::Usable($next,$window)
        }
        $durations += $started.ElapsedMilliseconds
    }
    $system=[NavigationProbe]::Find($window,'System')
    if($system -eq [IntPtr]::Zero){throw 'System appearance radio was not found'}
    $radioRect=New-Object NavigationProbe+RECT
    [NavigationProbe]::GetWindowRect($system,[ref]$radioRect)|Out-Null
    $radioHeight=($radioRect.bottom-$radioRect.top)*96.0/[NavigationProbe]::GetDpiForWindow($system)
    if($radioHeight -gt 40){throw "System radio wrapped to $radioHeight DIPs"}
    if(![NavigationProbe]::Checked($system)){throw 'System appearance radio did not select the OS theme'}
    Capture $window 'home-final.png'
    $hashes=Get-ChildItem (Join-Path $OutputDirectory 'push-*.png') | ForEach-Object {(Get-FileHash $_.FullName -Algorithm SHA256).Hash} | Select-Object -Unique
    if($hashes.Count -lt 6){throw "Transition screenshots did not produce at least six distinct frames"}
    Get-ChildItem (Join-Path $OutputDirectory 'push-*.png') | ForEach-Object {Assert-NoBlackBand $_.FullName}
    if($VerifyFade) {
        $before=[Drawing.Bitmap]::FromFile((Join-Path $OutputDirectory 'home-before.png'))
        $after=[Drawing.Bitmap]::FromFile((Join-Path $OutputDirectory 'push-final.png'))
        try {
            foreach($file in (Get-ChildItem (Join-Path $OutputDirectory 'push-*.png'))) {
                $frame=[Drawing.Bitmap]::FromFile($file.FullName)
                try {
                    $stablePixels=0; $changedPixels=0
                    for($y=85;$y -lt $before.Height-30;$y+=16) {
                        for($x=30;$x -lt $before.Width-30;$x+=16) {
                            $a=$before.GetPixel($x,$y); $b=$after.GetPixel($x,$y)
                            if($a.ToArgb() -eq $b.ToArgb()) {
                                $stablePixels++
                                $c=$frame.GetPixel($x,$y)
                                if([Math]::Abs([int]$a.R-$c.R) -gt 12 -or [Math]::Abs([int]$a.G-$c.G) -gt 12 -or [Math]::Abs([int]$a.B-$c.B) -gt 12){$changedPixels++}
                            }
                        }
                    }
                    if($stablePixels -lt 100){throw 'Insufficient matching endpoint pixels to validate fade'}
                    if($changedPixels -gt $stablePixels*0.03){throw "Fade flashed unchanged content in $($file.Name): $changedPixels / $stablePixels pixels"}
                } finally {$frame.Dispose()}
            }
        } finally {$before.Dispose();$after.Dispose()}
    }
    $stable=($durations | Where-Object {$_ -ge 150 -and $_ -le 450}).Count
    if(($durations | Where-Object {$_ -lt 140 -or $_ -gt 700}).Count -ne 0 -or $stable -lt 8){throw "Unexpected transition completion times: $durations"}
    "PASS DirectComposition navigation: 5 push/Back cycles, $($hashes.Count) distinct compositor frames, completion ms: $durations" | Tee-Object (Join-Path $OutputDirectory 'PASS.txt')
} catch {
    (($_ | Out-String)+"`n"+[NavigationProbe]::Dump($window)) | Set-Content (Join-Path $OutputDirectory 'FAIL.txt')
    throw
} finally {
    if($preview -and !$preview.HasExited){$preview.Kill();$preview.WaitForExit()}
}
