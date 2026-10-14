// Reads a simulator screenshot without adding a Python imaging dependency.
import Foundation
import ImageIO
import CoreGraphics

let args = CommandLine.arguments
guard args.count == 4,
      let x = Double(args[2]), let y = Double(args[3]),
      x >= 0, x < 1, y >= 0, y < 1,
      let source = CGImageSourceCreateWithURL(URL(fileURLWithPath: args[1]) as CFURL, nil),
      let image = CGImageSourceCreateImageAtIndex(source, 0, nil),
      let pixel = image.cropping(to: CGRect(x: Int(x * Double(image.width)),
          y: Int(y * Double(image.height)), width: 1, height: 1)) else {
    fatalError("Expected screenshot path and normalized x/y coordinates")
}
var bytes = [UInt8](repeating: 0, count: 4)
let context = CGContext(data: &bytes, width: 1, height: 1, bitsPerComponent: 8,
    bytesPerRow: 4, space: CGColorSpaceCreateDeviceRGB(),
    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
context.draw(pixel, in: CGRect(x: 0, y: 0, width: 1, height: 1))
print("[\(bytes[0]),\(bytes[1]),\(bytes[2]),\(bytes[3])]")
