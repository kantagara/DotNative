import XCTest

final class PerformanceTests: XCTestCase {
    private var app: XCUIApplication!
    private var generation = 0
    private var rowCount = 10000
    override func setUpWithError() throws {
        continueAfterFailure = false
        guard let bundle = ProcessInfo.processInfo.environment["BENCH_BUNDLE_ID"] else {
            throw XCTSkip("Set BENCH_BUNDLE_ID to the installed Release benchmark application")
        }
        #if os(macOS)
        guard let path = ProcessInfo.processInfo.environment["BENCH_APP_PATH"] else {
            throw XCTSkip("Set BENCH_APP_PATH to the exact Release .app")
        }
        app = XCUIApplication(url: URL(fileURLWithPath: path))
        #else
        app = XCUIApplication(bundleIdentifier: bundle)
        #endif
    }
    private var options: XCTMeasureOptions {
        let options = XCTMeasureOptions()
        options.iterationCount = Int(ProcessInfo.processInfo.environment["BENCH_ITERATIONS"] ?? "10") ?? 10
        return options
    }
    private func start() {
        generation = 0
        rowCount = 10000
        app.launch()
        XCTAssertTrue(app.staticTexts["Ready 10000 v4"].waitForExistence(timeout: 20))
        visible("Item 00000 · 0")
    }
    private func visible(_ title: String) {
        let rendered = NSPredicate { _, _ in self.app.buttons[title].firstMatch.isHittable }
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: rendered, object: nil)], timeout: 10), .completed,
                       "Expected visible row \(title); completion alone is insufficient")
    }
    private func click(_ name: String) {
        let button = app.buttons[name].firstMatch
        XCTAssertTrue(button.waitForExistence(timeout: 5))
        button.tap()
        generation += 1
        if name == "Prepend" { rowCount += 100 }
        if name == "Clear" { rowCount = 0 }
        if name == "Reset" { rowCount = 10000 }
        // AppKit exposes NSTextField content as its accessibility value. Exact
        // lookup works on both platforms; filtering only by label does not.
        XCTAssertTrue(app.staticTexts["Done \(generation) \(name) \(rowCount)"].waitForExistence(timeout: 5))
    }
    func testStartup() {
        measure(metrics: [XCTApplicationLaunchMetric(waitUntilResponsive: false)], options: options) {
            start()
        }
        app.terminate()
    }
    private func scroll(variable: Bool) {
        start()
        if variable { click("Variable") }
        click("Jump")
        visible("Item 05000 · 0")
        let list = app.collectionViews.firstMatch.exists ? app.collectionViews.firstMatch : app.scrollViews.firstMatch
        XCTAssertTrue(list.waitForExistence(timeout: 5))
        #if os(iOS)
        let metrics: [XCTMetric] = [XCTOSSignpostMetric.scrollingAndDecelerationMetric, XCTMemoryMetric(application: app)]
        #else
        // AppKit scroll views don't emit UIKit's scrolling signposts. Clock
        // duration is labelled as the complete UI-automation journey, never FPS.
        let metrics: [XCTMetric] = [XCTClockMetric(), XCTMemoryMetric(application: app)]
        #endif
        measure(metrics: metrics, options: options) {
            #if os(iOS)
            let viewport = list.frame
            XCTAssertEqual(viewport.width, 320, accuracy: 0.5, "Viewport must match the shared contract")
            XCTAssertEqual(viewport.height, 320, accuracy: 0.5, "Viewport must match the shared contract")
            func rows() -> [String: Double] {
                var result: [String: Double] = [:]
                for button in app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Item ")).allElementsBoundByIndex {
                    let frame = button.frame
                    if frame.minY > viewport.minY + 1 && frame.maxY < viewport.maxY - 1 && frame.intersects(viewport) {
                        result[button.label] = Double(frame.minY - viewport.minY)
                    }
                }
                return result
            }
            let initial = rows()
            var movements: [Double] = []
            for leg in 0..<10 {
                let before = rows()
                let lower = list.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.75))
                let upper = list.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.35))
                let from = leg < 5 ? lower : upper
                let to = leg < 5 ? upper : lower
                from.press(forDuration: 0.05, thenDragTo: to, withVelocity: XCUIGestureVelocity(rawValue: 320), thenHoldForDuration: 0.8)
                let after = rows()
                let deltas = before.keys.compactMap { key -> Double? in
                    guard let end = after[key] else { return nil }
                    return before[key]! - end
                }.sorted()
                XCTAssertFalse(deltas.isEmpty, "No common visible scroll anchor")
                guard !deltas.isEmpty else { return }
                XCTAssertLessThanOrEqual(deltas.last! - deltas.first!, 1, "Unstable row geometry")
                let moved = deltas[deltas.count / 2]
                XCTAssertGreaterThan(leg < 5 ? moved : -moved, Double(viewport.height) * 0.25)
                movements.append(moved)
            }
            let evidence: [String: Any] = ["variable": variable, "width": Double(viewport.width),
                "height": Double(viewport.height), "initialRows": initial, "movements": movements,
                "netPoints": movements.reduce(0, +)]
            let data = try! JSONSerialization.data(withJSONObject: evidence, options: [.sortedKeys])
            print("SCROLL_EVIDENCE " + String(data: data, encoding: .utf8)!)
            #else
            for _ in 0..<5 { list.swipeUp() }
            for _ in 0..<5 { list.swipeDown() }
            #endif
        }
        app.terminate()
    }
    func testFixedRows() { scroll(variable: false) }
    func testVariableRows() { scroll(variable: true) }
    func testMutations() {
        start(); click("Variable"); click("Jump"); visible("Item 05000 · 0")
        measure(metrics: [XCTClockMetric(), XCTMemoryMetric(application: app)], options: options) {
            for action in ["Update", "Expand", "Expand", "Prepend", "Reverse", "Reverse", "Clear", "Reset"] {
                click(action)
                if action == "Update" { visible("Item 05000 · 1") }
                if action == "Clear" { XCTAssertEqual(app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Item ")).count, 0) }
                if action == "Reset" { visible("Item 00000 · 0") }
            }
            click("Jump")
            visible("Item 05000 · 0")
        }
        app.terminate()
    }
}
