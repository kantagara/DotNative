package com.dotnative.plugins
import android.app.Activity
fun NativeProbePlugin(activity: Activity) {
    NativeChannels.channel("dotnative.plugins.nativeprobe").handle("read") { _, reply ->
        reply.success("Android: " + android.os.Build.MODEL)
    }
}