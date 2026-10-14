import Foundation

@main struct PluginCodecTests {
  static func main() throws {
    var data = Data()
    try PluginCodec.encode(.double(0.125), into: &data)
    precondition(Array(data) == [8, 0, 0, 0, 0, 0, 0, 192, 63])
    var decoder = PluginCodec(data)
    guard case .double(let number) = try decoder.value() else { fatalError("Wrong tag") }
    precondition(number == 0.125 && decoder.exhausted)
    var deep: PluginValue = .null
    for _ in 0..<18 { deep = .list([deep]) }
    for invalid in [
      deep, .list(Array(repeating: .null, count: 4097)), .bytes(Data(count: 1_048_589)),
    ] {
      data = Data()
      do {
        try PluginCodec.encode(invalid, into: &data)
        fatalError("Invalid reply accepted")
      } catch {}
    }
    for bytes: [UInt8] in [[8, 0], [4, 1, 0, 0, 0, 255]] {
      decoder = PluginCodec(Data(bytes))
      do {
        _ = try decoder.value()
        fatalError("Invalid payload accepted")
      } catch {}
    }
    print(
      "PASS Swift codec: double golden bytes, UTF-8, truncated data, depth and collection limits")
  }
}
