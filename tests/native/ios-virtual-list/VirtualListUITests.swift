import XCTest
final class VirtualListUITests: XCTestCase {
    func testRapidDirectionChangesKeepViewportPopulated() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.dotnative.preview.interactionapp")
        app.launch()
        XCTAssertTrue(app.buttons["Row 0"].waitForExistence(timeout: 20))
        let collection = app.collectionViews.firstMatch
        XCTAssertTrue(collection.exists)
        func covered() {
            let viewport = collection.frame.intersection(app.frame)
            XCTAssertGreaterThan(viewport.height, 100)
            let intervals = collection.cells.allElementsBoundByIndex
                .map { $0.frame.intersection(viewport) }
                .filter { !$0.isNull && $0.height > 0 }
                .sorted { $0.minY < $1.minY }
            var bottom = viewport.minY
            XCTAssertFalse(intervals.isEmpty, "Fast scrolling left an empty viewport")
            for interval in intervals {
                XCTAssertLessThanOrEqual(interval.minY, bottom + 2, "Gap between realized rows")
                bottom = max(bottom, interval.maxY)
            }
            XCTAssertGreaterThanOrEqual(bottom, viewport.maxY - 2, "Rows do not cover the viewport")
            XCTAssertLessThan(app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Row '")).count, 40)
        }
        for _ in 0..<3 {
            app.buttons["Jump 5000"].tap()
            XCTAssertTrue(app.buttons["Row 5000"].waitForExistence(timeout: 10))
            covered()
            for _ in 0..<3 { collection.swipeUp(velocity: .fast); covered() }
            for _ in 0..<3 { collection.swipeDown(velocity: .fast); covered() }
            let button = app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Row '")).allElementsBoundByIndex.first { $0.isHittable }
            XCTAssertNotNil(button)
            if let button = button {
                let id = String(button.label.dropFirst(4))
                button.tap()
                XCTAssertTrue(app.staticTexts["Selected \(id)"].waitForExistence(timeout: 5), "Recycled row dispatched the wrong callback")
            }
        }
    }
    func testVirtualWindowAndAnchoring() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.dotnative.preview.interactionapp")
        app.launch()
        XCTAssertTrue(app.buttons["Row 0"].waitForExistence(timeout: 20))
        var mutation = 0
        func mutate(_ action: String) {
            app.buttons[action].tap()
            mutation += 1
            XCTAssertTrue(app.staticTexts["Mutation \(mutation)"].waitForExistence(timeout: 5))
        }
        func rowCount() -> Int { app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Row '")).count }
        XCTAssertLessThan(rowCount(), 24)
        let collection = app.collectionViews.firstMatch
        XCTAssertTrue(collection.exists)
        let originalCells = Set(collection.cells.allElementsBoundByIndex.map { $0.identifier })
        XCTAssertTrue(originalCells.contains { $0.hasPrefix("dotnative-virtual-cell-") })
        app.buttons["Jump 5000"].tap()
        XCTAssertTrue(app.buttons["Row 5000"].waitForExistence(timeout: 10))
        XCTAssertFalse(app.buttons["Row 0"].exists)
        let reusedCells = Set(collection.cells.allElementsBoundByIndex.map { $0.identifier })
        XCTAssertFalse(originalCells.intersection(reusedCells).isEmpty, "UICollectionView must reuse native cells")
        let row = app.buttons["Row 5000"]
        row.tap()
        XCTAssertTrue(app.staticTexts["Selected 5000"].waitForExistence(timeout: 5))
        let y = row.frame.minY
        mutate("Prepend")
        XCTAssertEqual(row.frame.minY, y, accuracy: 2)
        let next = app.buttons["Row 5001"]
        let nextY = next.frame.minY
        mutate("Grow")
        let grown = NSPredicate { _, _ in next.frame.minY > nextY + 30 }
        expectation(for: grown, evaluatedWith: nil)
        waitForExpectations(timeout: 5)
        XCTAssertEqual(row.frame.minY, y, accuracy: 2)
        mutate("Grow")
        let shrunk = NSPredicate { _, _ in abs(next.frame.minY - nextY) <= 2 }
        expectation(for: shrunk, evaluatedWith: nil)
        waitForExpectations(timeout: 5)
        for _ in 0..<2 {
            mutate("Reverse")
            let anchored = NSPredicate { _, _ in row.exists && abs(row.frame.minY - y) <= 2 }
            expectation(for: anchored, evaluatedWith: nil)
            waitForExpectations(timeout: 5)
        }
        collection.swipeUp()
        XCTAssertLessThan(rowCount(), 24)
        app.buttons["End"].tap()
        XCTAssertTrue(app.buttons["Row 9999"].waitForExistence(timeout: 10))
        XCTAssertLessThan(rowCount(), 24)
        app.buttons["Clear"].tap()
        XCTAssertTrue(app.staticTexts["Empty"].waitForExistence(timeout: 5))
        XCTAssertEqual(rowCount(), 0)
    }
}
