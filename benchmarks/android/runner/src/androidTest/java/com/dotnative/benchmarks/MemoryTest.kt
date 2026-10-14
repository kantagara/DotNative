package com.dotnative.benchmarks

import android.os.Bundle
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import org.junit.Test
import org.junit.runner.RunWith

/** Separate from frame/startup capture: dumpsys must not perturb those metrics. */
@RunWith(AndroidJUnit4::class)
class MemoryTest {
    @Test fun retainedMemory() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val arguments = InstrumentationRegistry.getArguments()
        val target = requireNotNull(arguments.getString("targetPackage"))
        val device = UiDevice.getInstance(instrumentation)
        var generation = 0
        var count = 10000
        fun visible(title: String) {
            val row = device.wait(Until.findObject(By.text(title)), 10_000)
            checkNotNull(row) { "Missing visible $title" }
            check(!row.visibleBounds.isEmpty) { "Offscreen $title" }
        }
        fun click(action: String) {
            checkNotNull(device.wait(Until.findObject(By.text(action)), 5_000)).click()
            generation++
            if (action == "Clear") count = 0
            if (action == "Reset") count = 10000
            check(device.wait(Until.hasObject(By.text("Done $generation $action $count")), 5_000))
            device.waitForIdle()
        }
        fun snapshot(phase: String) {
            device.waitForIdle()
            Thread.sleep(2000)
            val memory = device.executeShellCommand("dumpsys meminfo $target")
            check(!memory.contains("No process found")) { "Missing live process in $phase" }
            instrumentation.sendStatus(2, Bundle().apply { putString("memory_$phase", memory) })
        }
        device.executeShellCommand("am force-stop $target")
        try {
            val launch = checkNotNull(instrumentation.context.packageManager.getLaunchIntentForPackage(target))
            instrumentation.context.startActivity(launch)
            check(device.wait(Until.hasObject(By.text("Ready 10000 v4")), 15_000))
            visible("Item 00000 · 0")
            snapshot("initial_fixed")
            click("Jump"); visible("Item 05000 · 0"); snapshot("jump_fixed")
            click("Variable"); click("Jump"); visible("Item 05000 · 0"); snapshot("jump_variable")
            click("Clear"); click("Reset"); click("Jump"); visible("Item 05000 · 0"); snapshot("clear_reset_variable")
        } finally {
            device.executeShellCommand("am force-stop $target")
        }
    }
}
