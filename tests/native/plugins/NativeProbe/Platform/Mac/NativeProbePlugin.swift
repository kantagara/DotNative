import AppKit
@MainActor enum NativeProbePlugin {
    static func register() {
        NativeChannels.channel("dotnative.plugins.nativeprobe").handle("read") { _, reply in
            reply.success(.string("macOS: " + ProcessInfo.processInfo.operatingSystemVersionString))
        }
    }
}