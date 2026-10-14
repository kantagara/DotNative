#include <DotNativeEvents.hpp>
#include <DotNativePlugins.hpp>
#include <cassert>
#include <iostream>
#include <limits>
#include <thread>
static void (*dispatch)(const uint8_t *, size_t);
static std::mutex result_mutex;
static std::vector<dotnative::Value::Bytes> results;
extern "C" int32_t dotnative_register_channel(uint32_t id,
                                              void (*callback)(const uint8_t *,
                                                               size_t)) {
  assert(id == 42);
  dispatch = callback;
  return 0;
}
extern "C" int32_t dotnative_plugin_reply(const uint8_t *bytes, size_t count) {
  std::lock_guard<std::mutex> lock(result_mutex);
  results.emplace_back(bytes, bytes + count);
  return 0;
}
using namespace dotnative;
static void Send(uint64_t id, uint32_t kind, Value args = {},
                 std::string method = "read") {
  Value::Bytes packet;
  Codec::Number(packet, id, 8);
  Codec::Number(packet, 42, 4);
  Codec::Number(packet, kind, 4);
  if (kind == 1) {
    auto payload =
        Codec::Encode(Value::Map{{"method", method}, {"arguments", args}});
    packet.insert(packet.end(), payload.begin(), payload.end());
  }
  dispatch(packet.data(), packet.size());
}
int main() {
  auto bytes = Codec::Encode(0.125);
  assert((bytes == Value::Bytes{8, 0, 0, 0, 0, 0, 0, 0xc0, 0x3f}));
  assert(Codec::Decode(bytes.data(), bytes.size()).As<double>() == 0.125);
  for (Value v : {Value(), Value(true), Value(false), Value(int64_t(-1)),
                  Value(std::numeric_limits<int64_t>::min()), Value("čćž 😄"),
                  Value(Value::Bytes{0, 255}), Value(Value::List{1, "x"}),
                  Value(Value::Map{{"x", 2.5}})}) {
    auto encoded = Codec::Encode(v);
    auto decoded = Codec::Decode(encoded.data(), encoded.size());
    assert(Codec::Encode(decoded) == encoded);
  }
  for (const auto &invalid : std::vector<Value::Bytes>{
           {4, 1, 0, 0, 0, 255},
           {5, 255, 255, 255, 127},
           {8, 0},
           {0, 0},
           {6, 2, 0, 0, 0, 4, 1, 0, 0, 0, 'x', 0, 4, 1, 0, 0, 0, 'x', 0}}) {
    bool rejected = false;
    try {
      Codec::Decode(invalid.data(), invalid.size());
    } catch (...) {
      rejected = true;
    }
    assert(rejected);
  }
  bool rejected = false;
  try {
    Codec::Encode(Value::Bytes(Codec::Limit));
  } catch (...) {
    rejected = true;
  }
  assert(rejected);
  PluginRegistrar registrar(reinterpret_cast<void *>(1));
  assert(registrar.NativeWindow() != nullptr);
  auto &channel = registrar.Channel(42);
  std::shared_ptr<Reply> pending;
  int cancellations = 0, resets = 0;
  channel.Handle("read", [&](const Value &value, std::shared_ptr<Reply> reply) {
    assert(value.As<std::string>() == "retained");
    pending = reply;
    reply->OnCancel = [&] { ++cancellations; };
  });
  channel.OnReset = [&] { ++resets; };
  Send(1, 1, "retained");
  assert(results.empty());
  Send(1, 2);
  assert(cancellations == 1);
  assert(!pending->Success("late"));
  Send(2, 1, "retained");
  std::thread worker([reply = pending] {
    assert(reply->Success(2.5));
    assert(!reply->Success(3.5));
  });
  worker.join();
  assert(results.size() == 1);
  assert(Codec::Decode(results[0].data() + 12, results[0].size() - 12)
             .As<double>() == 2.5);
  Send(3, 1, "retained");
  Send(0, 3);
  assert(resets == 1 && cancellations == 2);
  assert(!pending->Success());
  assert(detail::pending.empty());
  int stream_cancellations = 0;
  std::shared_ptr<EventSink> sink;
  EventChannel events(
      registrar, 42,
      [&](const Value &, EventSink value) {
        sink = std::make_shared<EventSink>(std::move(value));
      },
      [&](const Value &) { ++stream_cancellations; });
  auto last = [] {
    const auto &p = results.back();
    return Codec::Decode(p.data() + 12, p.size() - 12).As<Value::Map>();
  };
  Send(10, 1, {}, "listen");
  auto before = results.size();
  Send(11, 1, {}, "next");
  assert(results.size() == before);
  std::thread event_worker([sink] { sink->Success(17); });
  event_worker.join();
  assert(last().at("value").As<int64_t>() == 17);
  sink->EndOfStream();
  Send(12, 1, {}, "next");
  assert(last().at("event").As<std::string>() == "end");
  Send(13, 1, {}, "cancel");
  assert(stream_cancellations == 1);
  auto old = sink;
  Send(14, 1, {}, "listen");
  old->Success(100);
  before = results.size();
  Send(15, 1, {}, "next");
  assert(results.size() == before);
  Send(15, 2);
  Send(16, 1, {}, "cancel");
  assert(stream_cancellations == 2);
  Send(17, 1, {}, "listen");
  for (int i = 0; i < 65; i++)
    sink->Success(i);
  Send(18, 1, {}, "next");
  assert(last().at("code").As<std::string>() == "overflow");
  Send(0, 3);
  assert(stream_cancellations == 3 && detail::pending.empty());
  std::cout << "PASS C++ events: worker delivery, FIFO end, cancel, stale "
               "sink, overflow, reset\n";
  std::cout << "PASS C++ plugin SDK: wire values, bounds, native context, "
               "async reply, duplicate, cancel and reset\n";
}
