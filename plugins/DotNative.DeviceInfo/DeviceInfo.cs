using System;
using System.Runtime.InteropServices;

namespace DotNative.Plugins;

public static partial class DeviceInfo
{
    public static int IosMajorVersion
    {
        get
        {
            if (!OperatingSystem.IsIOS())
                throw new PlatformNotSupportedException(
                    "This plugin currently implements iOS only."
                );
            return GetIosMajorVersion();
        }
    }

    [LibraryImport("__Internal", EntryPoint = "dotnative_deviceinfo_ios_major_version")]
    private static partial int GetIosMajorVersion();
}
