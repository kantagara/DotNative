# DotNative.DeviceInfo

An optional NuGet plugin with its own C# project and Objective-C implementation.
Reference it from a DotNative.App project, then read
`DotNative.Plugins.DeviceInfo.IosMajorVersion`. No Xcode edits or copied native
files are needed. The app tooling discovers its MSBuild native-source declaration,
compiles the source and links its C export into the application. This implementation
supports iOS only; other OS calls throw PlatformNotSupportedException.
