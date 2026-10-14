Native Android controls, JNI, and experimental .NET 9 Bionic NativeAOT.
Run `dotnet run -r linux-bionic-arm64 -p:DisableUnsupportedError=true` from
the app directory, with an ARM64 emulator booted and JDK 17, SDK 36, NDK 29 installed.
This does not yet supply a general Kotlin/Java NuGet plugin build contract.
