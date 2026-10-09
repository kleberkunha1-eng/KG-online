param(
    [string]$ClientRoot = 'E:\NEW SV\Client',
    [string]$Destination = (Join-Path $PSScriptRoot '..\Assets\Resources\PKOCursors')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class OriginalCursorImage {
    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint X, Y;
        public IntPtr Mask, Color;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadCursorFromFile(string file);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetIconInfo(IntPtr handle, out IconInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyCursor(IntPtr handle);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr handle);
}
'@
[IO.Directory]::CreateDirectory($Destination) | Out-Null
$records = @()
foreach ($name in @('mouseon', 'chat', 'drag', 'attack')) {
    $source = Join-Path $ClientRoot "cursor\$name.ani"
    $handle = [OriginalCursorImage]::LoadCursorFromFile($source)
    if ($handle -eq [IntPtr]::Zero) { throw "Cannot load original cursor $name; Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    $info = New-Object OriginalCursorImage+IconInfo
    try {
        if (-not [OriginalCursorImage]::GetIconInfo($handle, [ref]$info)) { throw "Cannot read cursor metadata: $name" }
        $icon = [Drawing.Icon]::FromHandle($handle)
        $bitmap = $icon.ToBitmap()
        try {
            $bitmap.Save((Join-Path $Destination "$name.png"), [Drawing.Imaging.ImageFormat]::Png)
            $records += [pscustomobject]@{ Name=$name; Width=$bitmap.Width; Height=$bitmap.Height; HotspotX=$info.X; HotspotY=$info.Y }
        } finally { $bitmap.Dispose(); $icon.Dispose() }
    } finally {
        if ($info.Mask -ne [IntPtr]::Zero) { [OriginalCursorImage]::DeleteObject($info.Mask) | Out-Null }
        if ($info.Color -ne [IntPtr]::Zero) { [OriginalCursorImage]::DeleteObject($info.Color) | Out-Null }
        [OriginalCursorImage]::DestroyCursor($handle) | Out-Null
    }
}
$records | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Destination 'cursor-hotspots.json') -Encoding UTF8
$records | Format-Table
