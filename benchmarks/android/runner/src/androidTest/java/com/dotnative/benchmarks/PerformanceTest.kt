package com.dotnative.benchmarks

import java.io.ByteArrayOutputStream
import android.graphics.Rect
import android.view.accessibility.AccessibilityNodeInfo
import android.os.Bundle
import android.os.SystemClock
import android.view.MotionEvent
import org.json.JSONObject
import androidx.benchmark.macro.*
import androidx.benchmark.macro.junit4.MacrobenchmarkRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.Configurator
import androidx.test.uiautomator.Until
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class PerformanceTest {
    init {
        // Geometry reads must not add the default multi-second idle wait per node.
        // Each drag separately waits for a stable accessibility tree.
        Configurator.getInstance().waitForIdleTimeout = 100
    }
    @get:Rule val benchmark = MacrobenchmarkRule()
    private val arguments = InstrumentationRegistry.getArguments()
    private val target = requireNotNull(arguments.getString("targetPackage"))
    private val repetitions = arguments.getString("iterations", "10").toInt().also { require(it >= 1) }

    private fun MacrobenchmarkScope.ready() {
        check(device.wait(Until.hasObject(By.text("Ready 10000 v4")), 15_000)) { "Missing benchmark fixture" }
        visible("Item 00000 · 0")
        InstrumentationRegistry.getInstrumentation().uiAutomation.waitForIdle(500, 5000)
    }
    private fun MacrobenchmarkScope.visible(title: String) {
        check(device.wait(Until.hasObject(By.text(title)), 10_000)) { "Expected visible row $title; completion alone is insufficient" }
        val row = checkNotNull(device.findObject(By.text(title)))
        check(!row.visibleBounds.isEmpty) { "Expected row is outside the viewport: $title" }
    }
    private fun MacrobenchmarkScope.click(label: String) {
        val previous = device.findObject(By.textStartsWith("Done "))?.text
        val button = device.wait(Until.findObject(By.text(label)), 5_000)
        checkNotNull(button) { "Missing $label" }.click()
        if (previous != null) check(device.wait(Until.gone(By.text(previous)), 5_000)) { "Stale completion for $label" }
        val finished = device.wait(Until.hasObject(By.textContains(" $label ")), 5_000)
        if (!finished) {
            val snapshot = ByteArrayOutputStream()
            device.dumpWindowHierarchy(snapshot)
            InstrumentationRegistry.getInstrumentation().sendStatus(2, Bundle().apply { putString("failureHierarchy", snapshot.toString("UTF-8")) })
        }
        check(finished) { "Action $label did not finish" }
        InstrumentationRegistry.getInstrumentation().uiAutomation.waitForIdle(500, 5000)
    }
    @Test fun startup() = benchmark.measureRepeated(
        packageName = target, metrics = listOf(StartupTimingMetric()), iterations = repetitions,
        compilationMode = CompilationMode.None(), startupMode = StartupMode.COLD,
        setupBlock = { pressHome() },
        measureBlock = { startActivityAndWait(); ready() }
    )
    private fun scrolling(variable: Boolean) = benchmark.measureRepeated(
        packageName = target, metrics = listOf(FrameTimingMetric()), iterations = repetitions,
        compilationMode = CompilationMode.None(),
        setupBlock = { killProcess(); startActivityAndWait(); ready(); if (variable) click("Variable"); click("Jump"); visible("Item 05000 · 0") },
        measureBlock = {
            val list = device.wait(Until.findObject(By.scrollable(true)), 5_000)
            checkNotNull(list) { "Native scroll container unavailable" }
            val bounds = list.visibleBounds
            val x = bounds.centerX()
            val distance = bounds.height() * 2 / 5
            val top = bounds.centerY() - distance / 2
            val bottom = top + distance
            fun rows(): Map<String, Int> {
                // Read a single active-window snapshot; hierarchy dumping adds idle waits.
                val result = mutableMapOf<String, Int>()
                fun visit(node: AccessibilityNodeInfo) {
                    try {
                        val title = node.text?.toString() ?: ""
                        if (title.startsWith("Item ") && node.isVisibleToUser) {
                            val rect = Rect()
                            node.getBoundsInScreen(rect)
                            if (rect.top > bounds.top + 2 && rect.bottom < bounds.bottom - 2 && !rect.isEmpty)
                                result[title] = rect.top
                        }
                        repeat(node.childCount) { index -> node.getChild(index)?.let { visit(it) } }
                    } finally { node.recycle() }
                }
                visit(checkNotNull(InstrumentationRegistry.getInstrumentation().uiAutomation.rootInActiveWindow))
                return result
            }
            val initialRows = rows().mapValues { it.value - bounds.top }
            val movements = mutableListOf<Int>()
            val durations = mutableListOf<Long>()
            val corrections = mutableListOf<Int>()
            val targetDistance = bounds.height() * 3 / 8
            repeat(10) { leg ->
                val before = rows()
                val from = if (leg < 5) bottom else top
                val to = if (leg < 5) top else bottom
                val start = SystemClock.uptimeMillis()
                var pointerDown = false
                var lastY = from
                fun inject(action: Int, y: Int) {
                    lastY = y
                    if (action == MotionEvent.ACTION_DOWN) pointerDown = true
                    if (action == MotionEvent.ACTION_UP) pointerDown = false
                    val event = MotionEvent.obtain(start, SystemClock.uptimeMillis(), action, x.toFloat(), y.toFloat(), 0)
                    try { check(InstrumentationRegistry.getInstrumentation().uiAutomation.injectInputEvent(event, true)) }
                    finally { event.recycle() }
                }
                fun deadline(offset: Long) {
                    val remaining = start + offset - SystemClock.uptimeMillis()
                    if (remaining > 0) SystemClock.sleep(remaining)
                }
                try {
                    inject(MotionEvent.ACTION_DOWN, from)
                    repeat(24) { step ->
                        deadline(16L * (step + 1))
                        inject(MotionEvent.ACTION_MOVE, from + (to - from) * (step + 1) / 24)
                    }
                    fun displacement(): Int {
                        val current = rows()
                        val values = before.keys.intersect(current.keys).map { before.getValue(it) - current.getValue(it) }
                        check(values.isNotEmpty()) { "No common anchor while held" }
                        check(values.maxOrNull()!! - values.minOrNull()!! <= 2) { "Unstable held geometry: $values" }
                        return values.sorted()[values.size / 2]
                    }
                    val wanted = if (leg < 5) targetDistance else -targetDistance
                    var releaseY = to
                    var correction = 0
                    repeat(8) { step -> deadline(416L + 32L * step); inject(MotionEvent.ACTION_MOVE, to + if (step % 2 == 0) 1 else 0) }
                    val heldMovements = mutableListOf<Int>()
                    repeat(3) {
                        InstrumentationRegistry.getInstrumentation().uiAutomation.waitForIdle(100, 3000)
                        val observed = displacement()
                        heldMovements.add(observed)
                        val adjust = wanted - observed
                        check(kotlin.math.abs(adjust) <= distance / 4) { "Excessive correction: $adjust" }
                        releaseY -= adjust
                        correction += adjust
                        inject(MotionEvent.ACTION_MOVE, releaseY)
                        val settledStart = SystemClock.uptimeMillis()
                        repeat(4) { step ->
                            deadline(settledStart - start + 32L * (step + 1))
                            inject(MotionEvent.ACTION_MOVE, releaseY + if (step % 2 == 0) 1 else 0)
                        }
                    }
                    // Hold long enough to remove residual velocity, then release on a fixed deadline.
                    check(SystemClock.uptimeMillis() - start < 2080) { "Geometry sampling missed the hold window: ${SystemClock.uptimeMillis() - start}ms" }
                    repeat(10) { step -> deadline(2080L + 32L * step); inject(MotionEvent.ACTION_MOVE, releaseY + if (step % 2 == 0) 1 else 0) }
                    deadline(2400)
                    val duration = SystemClock.uptimeMillis() - start
                    inject(MotionEvent.ACTION_UP, releaseY)
                    check(duration in 2400..2440) { "Input schedule slipped: ${duration}ms" }
                    durations.add(duration)
                    corrections.add(correction)
                    InstrumentationRegistry.getInstrumentation().uiAutomation.waitForIdle(100, 3000)
                    val after = rows()
                    val deltas = before.keys.intersect(after.keys).map { before.getValue(it) - after.getValue(it) }
                    check(deltas.isNotEmpty()) { "No common visible anchor for leg $leg" }
                    check(deltas.maxOrNull()!! - deltas.minOrNull()!! <= 2) { "Unstable row geometry: $deltas" }
                    val moved = deltas.sorted()[deltas.size / 2]
                    check(kotlin.math.abs(moved - wanted) <= 1) { "Actual distance missed target: $moved / $wanted; correction=$correction; held=$heldMovements" }
                    movements.add(moved)
                } finally {
                    if (pointerDown) runCatching { inject(MotionEvent.ACTION_UP, lastY) }
                }
            }
            val evidence = JSONObject().put("variable", variable).put("width", bounds.width())
                .put("height", bounds.height()).put("gesturePixels", distance)
                .put("targetPixels", targetDistance).put("corrections", org.json.JSONArray(corrections))
                .put("durationMs", org.json.JSONArray(durations)).put("initialRows", JSONObject(initialRows)).put("movements", org.json.JSONArray(movements)).put("netPixels", movements.sum())
            InstrumentationRegistry.getInstrumentation().sendStatus(2, Bundle().apply { putString("scrollEvidence", evidence.toString()) })
        }
    )
    @Test fun fixedRows() = scrolling(false)
    @Test fun variableRows() = scrolling(true)
    @Test fun mutations() = benchmark.measureRepeated(
        packageName = target, metrics = listOf(FrameTimingMetric()), iterations = repetitions,
        compilationMode = CompilationMode.None(),
        setupBlock = { killProcess(); startActivityAndWait(); ready(); click("Variable"); click("Jump"); visible("Item 05000 · 0") },
        measureBlock = {
            for (action in listOf("Update", "Expand", "Expand", "Prepend", "Reverse", "Reverse", "Clear", "Reset")) {
                click(action)
                when (action) {
                    "Update" -> visible("Item 05000 · 1")
                    "Clear" -> check(device.wait(Until.gone(By.textStartsWith("Item ")), 5_000)) { "Clear retained visible rows" }
                    "Reset" -> visible("Item 00000 · 0")
                }
            }
            click("Jump")
            visible("Item 05000 · 0")
        }
    )
}
