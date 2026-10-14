param([Parameter(Mandatory=$true)][string]$AppDirectory,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Remove-Item (Join-Path $OutputDirectory 'PASS.txt'),(Join-Path $OutputDirectory 'FAIL.txt') -ErrorAction SilentlyContinue
Start-Transcript -Path (Join-Path $OutputDirectory 'input.log') -Force
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class InputProbe {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left,top,right,bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx,dy; public uint data,flags,time; public UIntPtr extra; }
 [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort key,scan; public uint flags,time; public UIntPtr extra; }
 [StructLayout(LayoutKind.Explicit,Size=40)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mouse; [FieldOffset(8)] public KEYBDINPUT keyboard; }
 [StructLayout(LayoutKind.Sequential)] public struct GUIINFO { public uint size,flags; public IntPtr active,focus,capture,menu,move,caret; public RECT rect; }
 [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread,ref GUIINFO info);
 public static IntPtr Focus(IntPtr window) {uint pid;uint thread=GetWindowThreadProcessId(window,out pid);var info=new GUIINFO();info.size=(uint)Marshal.SizeOf(typeof(GUIINFO));if(!GetGUIThreadInfo(thread,ref info))throw new Exception("GetGUIThreadInfo failed");return info.focus;}
 public delegate bool EnumProc(IntPtr hwnd,IntPtr param);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr param);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumProc callback,IntPtr param);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,UIntPtr count,StringBuilder text,uint flags,uint timeout,out UIntPtr result);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int max);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
 [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
 [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
 [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc,int x,int y);
 [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr hwnd,out uint key,out byte alpha,out uint flags);
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] inputs,int size);
 [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hwnd);
 public static IntPtr Window(int pid) { IntPtr found=IntPtr.Zero; EnumWindows((h,p)=>{uint id;GetWindowThreadProcessId(h,out id);if(id==pid && Text(h).StartsWith("DotNative Preview")){found=h;return false;}return true;},IntPtr.Zero);return found; }
 public static string Dump(IntPtr window) {if(window==IntPtr.Zero)return "Preview window not found";var text=new StringBuilder();EnumChildWindows(window,(h,p)=>{string label=Text(h);if(label.Length>0)text.AppendLine(h+": "+label);return true;},IntPtr.Zero);return text.ToString();}
 public static string Text(IntPtr h) {var s=new StringBuilder(512);UIntPtr result;SendMessageTimeout(h,13,(UIntPtr)s.Capacity,s,2,200,out result);return s.ToString();}
 public static IntPtr Find(IntPtr window,string text) {IntPtr found=IntPtr.Zero;EnumChildWindows(window,(h,p)=>{if(Text(h)==text){found=h;return false;}return true;},IntPtr.Zero);return found;}
 public static IntPtr Class(IntPtr window,string name) {IntPtr found=IntPtr.Zero;EnumChildWindows(window,(h,p)=>{var s=new StringBuilder(128);GetClassName(h,s,s.Capacity);if(s.ToString()==name){found=h;return false;}return true;},IntPtr.Zero);return found;}
 public static IntPtr Toggle(IntPtr window) {IntPtr found=IntPtr.Zero;EnumChildWindows(window,(h,p)=>{var s=new StringBuilder(128);GetClassName(h,s,s.Capacity);if(s.ToString()=="Button" && (GetWindowLongPtr(h,-16).ToInt64()&15)==3){found=h;return false;}return true;},IntPtr.Zero);return found;}
 public static int Alpha(IntPtr control) {uint key,flags;byte alpha;return GetLayeredWindowAttributes(GetParent(control),out key,out alpha,out flags)?alpha:255;}
 public static uint Pixel(int x,int y) {var dc=GetDC(IntPtr.Zero);if(dc==IntPtr.Zero)throw new Exception("GetDC failed");try{return GetPixel(dc,x,y);}finally{ReleaseDC(IntPtr.Zero,dc);}}
 public static void Mouse(int x,int y,uint flags) {var i=new INPUT();i.mouse.dx=(int)((long)x*65535/(GetSystemMetrics(0)-1));i.mouse.dy=(int)((long)y*65535/(GetSystemMetrics(1)-1));i.mouse.flags=flags|0x8000|1;if(SendInput(1,new[]{i},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Exception("SendInput mouse: "+Marshal.GetLastWin32Error());}
 public static void Key(ushort key,bool up) {var i=new INPUT();i.type=1;i.keyboard.key=key;i.keyboard.flags=up?2u:0u;if(SendInput(1,new[]{i},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Exception("SendInput key failed");}
}
'@
[InputProbe]::SetProcessDPIAware() | Out-Null
function Await([string]$reason,[scriptblock]$check,[int]$timeout=8) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeout)
    do { if (& $check) { return }; Start-Sleep -Milliseconds 20 } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $reason`n$([InputProbe]::Dump($script:window))"
}
function Control([string]$text) {
    Await "control $text" { [InputProbe]::Find($script:window,$text) -ne [IntPtr]::Zero }
    return [InputProbe]::Find($script:window,$text)
}
function Point([IntPtr]$control) {
    $r = New-Object InputProbe+RECT
    [InputProbe]::GetWindowRect($control,[ref]$r) | Out-Null
    return @([int](($r.left+$r.right)/2),[int](($r.top+$r.bottom)/2))
}
function Mouse([IntPtr]$control,[uint32]$flags=0) { $p=Point $control; [InputProbe]::Mouse($p[0],$p[1],$flags) }
function Tap([IntPtr]$control) { Mouse $control 2; Start-Sleep -Milliseconds 80; Mouse $control 4 }
function Alpha([IntPtr]$control,[int]$expected) { Await "alpha $expected for $([InputProbe]::Text($control)); actual $([InputProbe]::Alpha($control))" { [Math]::Abs([InputProbe]::Alpha($control)-$expected) -le 2 } }
function Shadow([IntPtr]$control) {
    $r = New-Object InputProbe+RECT
    [InputProbe]::GetWindowRect($control,[ref]$r) | Out-Null
    Await "visible shadow outside $([InputProbe]::Text($control))" {
        $pixel=[InputProbe]::Pixel($r.left-5,[int](($r.top+$r.bottom)/2))
        (($pixel -band 255) -lt 230) -and ((($pixel -shr 8) -band 255) -lt 230) -and ((($pixel -shr 16) -band 255) -lt 230)
    }
}
function Overflow([IntPtr]$control) {
    $r = New-Object InputProbe+RECT
    [InputProbe]::GetWindowRect($control,[ref]$r) | Out-Null
    Write-Output "Overflow rect $($r.left),$($r.top),$($r.right),$($r.bottom) pixel $([InputProbe]::Pixel($r.left+2,[int](($r.top+$r.bottom)/2)))"
    Await "unclipped overflow marker pixels" {
        $pixel=[InputProbe]::Pixel($r.left+2,[int](($r.top+$r.bottom)/2))
        (($pixel -band 255) -gt 200) -and ((($pixel -shr 8) -band 255) -lt 80) -and ((($pixel -shr 16) -band 255) -gt 200)
    }
}
function Transform([IntPtr]$control) {
    $r = New-Object InputProbe+RECT
    [InputProbe]::GetWindowRect($control,[ref]$r) | Out-Null
    Await "DirectComposition scale/rotation pixels outside source HWND" {
        $pixel=[InputProbe]::Pixel($r.left-10,[int](($r.top+$r.bottom)/2))
        (($pixel -band 255) -gt 200) -and ((($pixel -shr 8) -band 255) -gt 70) -and ((($pixel -shr 8) -band 255) -lt 190) -and ((($pixel -shr 16) -band 255) -lt 80)
    }
}
$script:window=[IntPtr]::Zero
$preview=$null
$previous=[InputProbe]::GetForegroundWindow()
try {
    $exe=Join-Path $AppDirectory 'InteractionApp.exe'
    $smoke=Start-Process $exe -ArgumentList '--smoke' -WorkingDirectory $AppDirectory -RedirectStandardOutput (Join-Path $OutputDirectory 'smoke.log') -RedirectStandardError (Join-Path $OutputDirectory 'smoke-errors.log') -PassThru
    $null=$smoke.Handle
    if (-not $smoke.WaitForExit(30000)) { $smoke.Kill(); throw 'Native smoke timed out' }
    if ($smoke.ExitCode -ne 0) { throw "Native smoke failed: $($smoke.ExitCode)" }
    $preview=Start-Process $exe -WorkingDirectory $AppDirectory -RedirectStandardOutput (Join-Path $OutputDirectory 'preview.log') -RedirectStandardError (Join-Path $OutputDirectory 'preview-errors.log') -PassThru
    Await 'preview window' { [InputProbe]::Window($preview.Id) -ne [IntPtr]::Zero } 30
    $script:window=[InputProbe]::Window($preview.Id)
    [InputProbe]::SetWindowPos($window,[IntPtr]::Zero,20,20,700,([InputProbe]::GetSystemMetrics(1)-60),0) | Out-Null
    [InputProbe]::SetForegroundWindow($window) | Out-Null
    Await 'foreground input ownership' { [InputProbe]::GetForegroundWindow() -eq $window }
    Await 'fixture initial commit' { [InputProbe]::Find($script:window,'Test radio') -ne [IntPtr]::Zero } 30
    $radio=Control 'Test radio'; $button=Control 'Test button'; $reset=Control 'Reset'
    Shadow $button
    Overflow (Control 'Overflow marker')
    Transform (Control 'Transform anchor')
    [InputProbe]::Mouse(5,5,0); Alpha $radio 255
    Mouse $radio; Alpha $radio 204
    Mouse $radio 2; Alpha $radio 102
    Control 'Selections: 0' | Out-Null
    Mouse $radio 4; Control 'Selections: 1' | Out-Null; Alpha $radio 166
    if ((Control 'Test radio') -ne $radio) { throw 'Radio native identity changed' }
    Tap $reset; [InputProbe]::Mouse(5,5,0); Alpha $radio 255
    Mouse $button 2; Alpha $button 102
    [InputProbe]::Mouse(5,5,0); [InputProbe]::Mouse(5,5,4); Alpha $button 255
    Control 'Clicks: 0' | Out-Null
    Tap $button; Control 'Clicks: 1' | Out-Null
    Tap $reset; Tap $button; Tap $button; Control 'Clicks: 2' | Out-Null
    Tap (Control 'Disable soon'); Mouse $radio 2; Alpha $radio 102
    Alpha $radio 51; Mouse $radio 4; Control 'Selections: 0' | Out-Null
    if ([InputProbe]::IsWindowEnabled($radio)) { throw 'Radio not natively disabled' }
    Tap $reset; [InputProbe]::Mouse(5,5,0); Alpha $radio 255
    foreach ($control in @($radio,$button,(Control 'Test field'),[InputProbe]::Toggle($window),[InputProbe]::Class($window,'msctls_trackbar32'))) {
        Mouse $control; $expected=if($control -eq $radio -or $control -eq $button){204}elseif($control -eq (Control 'Test field')){191}else{179}
        Alpha $control $expected; [InputProbe]::Mouse(5,5,0); Alpha $control 255
    }
    $field=Control 'Test field'; Tap $field; [InputProbe]::Mouse(5,5,0)
    Await 'field owns native focus' { [InputProbe]::Focus($window) -eq $field }; Alpha $field 140
    [InputProbe]::Key(9,$false); [InputProbe]::Key(9,$true)
    Await 'Tab moves focus to switch' { [InputProbe]::Focus($window) -ne $field }
    $toggle=[InputProbe]::Toggle($window); Await 'Tab focuses switch, not just any control' { [InputProbe]::Focus($window) -eq $toggle }; Alpha $toggle 140; Alpha $field 255
    $slider=[InputProbe]::Class($window,'msctls_trackbar32')
    [InputProbe]::Key(9,$false); [InputProbe]::Key(9,$true)
    Await 'Tab moves focus to slider' { [InputProbe]::Focus($window) -eq $slider }; Alpha $slider 140; Alpha $toggle 255
    [InputProbe]::Key(16,$false); [InputProbe]::Key(9,$false); [InputProbe]::Key(9,$true); [InputProbe]::Key(16,$true)
    Await 'Shift+Tab restores switch focus' { [InputProbe]::Focus($window) -eq $toggle }; Alpha $toggle 140; Alpha $slider 255
    # Return to the radio through keyboard traversal, without setting focus directly.
    for($i=0;$i -lt 3;$i++){[InputProbe]::Key(16,$false);[InputProbe]::Key(9,$false);[InputProbe]::Key(9,$true);[InputProbe]::Key(16,$true);Start-Sleep -Milliseconds 80}
    Await 'reverse traversal reaches radio' { [InputProbe]::Focus($window) -eq $radio }
    [InputProbe]::Key(32,$false); Alpha $radio 102
    [InputProbe]::Key(32,$true); Control 'Selections: 1' | Out-Null; Alpha $radio 166
    Add-Type -AssemblyName System.Drawing
    $r=New-Object InputProbe+RECT; [InputProbe]::GetWindowRect($window,[ref]$r) | Out-Null
    $image=New-Object Drawing.Bitmap ($r.right-$r.left),($r.bottom-$r.top)
    $graphics=[Drawing.Graphics]::FromImage($image)
    $graphics.CopyFromScreen($r.left,$r.top,0,0,$image.Size)
    $image.Save((Join-Path $OutputDirectory 'window.png'));$graphics.Dispose();$image.Dispose()
    'PASS Windows system mouse/pixels: rendered outer shadow, unclipped descendant and DirectComposition transform, hover on five controls, held press, selection, rapid repeated click, drag-out, disable while held, reset, Tab/Shift+Tab focus styles, Space activation, stable radio identity' | Tee-Object (Join-Path $OutputDirectory 'PASS.txt')
} catch {
    $_ | Out-String | Set-Content (Join-Path $OutputDirectory 'FAIL.txt')
    throw
} finally {
    [InputProbe]::Mouse(5,5,4)
    if ($preview -and -not $preview.HasExited) { $preview.CloseMainWindow() | Out-Null; if(-not $preview.WaitForExit(10000)){$preview.Kill()} }
    [InputProbe]::SetForegroundWindow($previous) | Out-Null
    Stop-Transcript
}
