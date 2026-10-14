import Foundation
import UserNotifications

@main struct NotificationCompletionTests {
  @MainActor static func main() async {
    let observers = APNsObservers()
    let token = Data([1, 2, 3])
    var first = 0
    var second = 0
    var failures = 0
    observers.observe("one", registered: { value in precondition(value == token); first += 1 }, failed: { _ in failures += 1 })
    observers.observe("two", registered: { _ in second += 1 }, failed: { _ in failures += 1 })
    observers.didRegister(token)
    precondition(first == 1 && second == 1)
    observers.remove("one")
    observers.didFail("denied")
    precondition(failures == 1)
    observers.observe("two", registered: { _ in second += 10 }, failed: { _ in failures += 10 })
    precondition(second == 11) // replay to a late/replaced observer
    observers.didRegister(token)
    precondition(first == 1 && second == 21)
    var calls = 0
    var result: UNNotificationPresentationOptions = []
    let batch = NotificationCompletion(count: 2) { options in calls += 1; result = options }
    batch.receive(0, options: [.banner])
    batch.receive(0, options: [.badge]) // duplicate reply must not consume listener 2
    precondition(calls == 0)
    batch.receive(1, options: [.sound])
    precondition(calls == 1 && result == [.banner, .sound])
    batch.finish()
    batch.receive(1, options: [.badge])
    precondition(calls == 1)
    var empty = 0
    _ = NotificationCompletion(count: 0) { _ in empty += 1 }
    precondition(empty == 1)
    var timedOut = 0
    // No owner retains the batch: its timeout must retain the OS completion.
    _ = NotificationCompletion(count: 1) { _ in timedOut += 1 }
    try? await Task.sleep(nanoseconds: 5_300_000_000)
    precondition(timedOut == 1)
    print("PASS notification completion: fan-in, option union, duplicate/late replies, empty listeners, retained timeout")
  }
}
