import XCTest

final class NavigationUITests: XCTestCase {
    func testConfiguredTransitionsAndNativeBack() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.dotnative.preview.interactionapp")
        app.launch()
        XCTAssertTrue(app.buttons["Run timing suite"].waitForExistence(timeout: 15))
        app.buttons["Run timing suite"].tap()
        let result = app.staticTexts.matching(NSPredicate(format: "label BEGINSWITH 'PASS ' OR label BEGINSWITH 'FAIL '")).firstMatch
        XCTAssertTrue(result.waitForExistence(timeout: 90))
        XCTAssertEqual(result.label, "PASS 24 round trips")
        for _ in 0..<3 {
            app.buttons["Open native Back"].tap()
            XCTAssertTrue(app.staticTexts["Detail ready"].waitForExistence(timeout: 5))
            app.navigationBars.buttons["Home"].tap()
            XCTAssertTrue(app.buttons["Open native Back"].waitForExistence(timeout: 5))
        }
        verifySystemSwipe(app)
    }
    func testSystemSwipe() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.dotnative.preview.interactionapp")
        app.launch()
        XCTAssertTrue(app.buttons["Open system swipe"].waitForExistence(timeout: 15))
        verifySystemSwipe(app)
    }
    func testCustomInteractiveCancellationAndCompletion() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.dotnative.preview.interactionapp")
        app.launch()
        XCTAssertTrue(app.buttons["Open slide swipe"].waitForExistence(timeout: 15))
        for (index, button) in ["Open slide swipe", "Open native Back"].enumerated() {
            app.buttons[button].tap()
            XCTAssertTrue(app.staticTexts["Detail ready"].waitForExistence(timeout: 5))
            app.buttons["Edit retained state"].tap()
            XCTAssertTrue(app.staticTexts["Retained edits: 1"].exists)
            let start = app.coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.65))
            let partial = app.coordinate(withNormalizedOffset: CGVector(dx: 0.25, dy: 0.65))
            let phase = index == 0 ? "navigation-slide" : "navigation-fade"
            print("DOTNATIVE_HOLD_BEGIN {\"phase\":\"\(phase)\",\"x\":0.1,\"y\":0.7}")
            start.press(forDuration: 0.05, thenDragTo: partial, withVelocity: .slow, thenHoldForDuration: 4)
            print("DOTNATIVE_HOLD_END " + phase)
            XCTAssertTrue(app.staticTexts["Retained edits: 1"].waitForExistence(timeout: 5))
            XCTAssertTrue(app.staticTexts["Disposed: \(index)"].exists)
            let end = app.coordinate(withNormalizedOffset: CGVector(dx: 0.95, dy: 0.65))
            start.press(forDuration: 0.05, thenDragTo: end)
            XCTAssertTrue(app.buttons[button].waitForExistence(timeout: 5))
            XCTAssertTrue(app.staticTexts["Disposed: \(index + 1)"].waitForExistence(timeout: 5))
        }
        verifySystemSwipe(app)
    }
    private func verifySystemSwipe(_ app: XCUIApplication) {
        app.buttons["Open system swipe"].tap()
        XCTAssertTrue(app.staticTexts["Detail ready"].waitForExistence(timeout: 5))
        let start = app.coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.5))
        let partial = app.coordinate(withNormalizedOffset: CGVector(dx: 0.18, dy: 0.5))
        start.press(forDuration: 0.05, thenDragTo: partial, withVelocity: .slow, thenHoldForDuration: 0.5)
        XCTAssertTrue(app.staticTexts["Detail ready"].waitForExistence(timeout: 5))
        let end = app.coordinate(withNormalizedOffset: CGVector(dx: 0.95, dy: 0.5))
        start.press(forDuration: 0.05, thenDragTo: end)
        XCTAssertTrue(app.buttons["Open system swipe"].waitForExistence(timeout: 5))
    }
}
