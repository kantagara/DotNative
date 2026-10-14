#include <DotNativePlugins.hpp>
#include <windows.h>
void NativeProbePlugin(const dotnative::PluginRegistrar &registrar) {
  auto window = registrar.NativeWindow();
  registrar.Channel("dotnative.plugins.nativeprobe")
      .Handle("read", [window](const dotnative::Value &,
                               std::shared_ptr<dotnative::Reply> reply) {
        if (!window || !IsWindow(static_cast<HWND>(window))) {
          reply->Error("no_window", "Native window is unavailable");
          return;
        }
        reply->Success("Windows: pid=" + std::to_string(GetCurrentProcessId()));
      });
}