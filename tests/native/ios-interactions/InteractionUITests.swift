import XCTest
import Darwin

final class InteractionUITests: XCTestCase {
    func testTouchRoundTrip() {
        continueAfterFailure = false
        let app = XCUIApplication(bundleIdentifier: "com.dotnative.preview.interactionapp")
        app.launch()
        let radio = app.buttons["Test radio"]
        XCTAssertTrue(radio.waitForExistence(timeout: 15))
        let reset = app.buttons["Reset"]
        let button = app.buttons["Test button"]
        func label(_ text: String) { XCTAssertTrue(app.staticTexts[text].waitForExistence(timeout: 5), text) }
        func redBackground(_ green: CGFloat) {
            // Sample the unobscured right edge of the rendered control, in screenshot pixels.
            let image = app.screenshot().image
            let cg = image.cgImage!
            let point = CGPoint(x: radio.frame.maxX - 6, y: radio.frame.midY)
            let x = Int(point.x * CGFloat(cg.width) / image.size.width)
            let y = Int(point.y * CGFloat(cg.height) / image.size.height)
            let crop = cg.cropping(to: CGRect(x: x, y: y, width: 1, height: 1))!
            var pixel = [UInt8](repeating: 0, count: 4)
            let context = CGContext(data: &pixel, width: 1, height: 1, bitsPerComponent: 8,
                bytesPerRow: 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
            context.draw(crop, in: CGRect(x: 0, y: 0, width: 1, height: 1))
            XCTAssertGreaterThan(pixel[0], 240)
            XCTAssertEqual(CGFloat(pixel[1]), green, accuracy: 12)
            XCTAssertEqual(CGFloat(pixel[2]), green, accuracy: 12)
        }
        func hold(_ phase: String) {
            let point = ["phase": phase, "x": (radio.frame.maxX - 6) / app.frame.width,
                         "y": radio.frame.midY / app.frame.height] as [String: Any]
            let data = try! JSONSerialization.data(withJSONObject: point, options: [.sortedKeys])
            print("DOTNATIVE_HOLD_BEGIN " + String(data: data, encoding: .utf8)!)
            fflush(stdout)
            radio.press(forDuration: 8)
            print("DOTNATIVE_HOLD_END " + phase)
            fflush(stdout)
        }
        label("Selections: 0"); redBackground(0)
        radio.tap(); label("Selections: 1"); redBackground(89)
        reset.tap(); label("Selections: 0"); redBackground(0)
        hold("pressed"); label("Selections: 1"); redBackground(89)
        reset.tap(); label("Selections: 0")
        app.buttons["Disable soon"].tap()
        hold("disabled-held"); label("Selections: 0"); redBackground(204)
        reset.tap(); redBackground(0)
        let outside = app.coordinate(withNormalizedOffset: CGVector(dx: 0.95, dy: 0.95))
        radio.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)).press(forDuration: 0.2, thenDragTo: outside)
        label("Selections: 0"); redBackground(0)
        button.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)).press(forDuration: 0.2, thenDragTo: outside)
        label("Clicks: 0")
        button.tap(); label("Clicks: 1")
        app.buttons["Disable soon"].tap()
        let disabled = NSPredicate(format: "enabled == false")
        expectation(for: disabled, evaluatedWith: radio)
        waitForExpectations(timeout: 5)
        redBackground(204)
        radio.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)).tap()
        label("Selections: 0")
        reset.tap(); radio.tap(); label("Selections: 1"); redBackground(89)
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.lifetime = .keepAlways
        add(attachment)
    }
}
