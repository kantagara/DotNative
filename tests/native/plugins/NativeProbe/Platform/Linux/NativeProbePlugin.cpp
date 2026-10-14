#include <DotNativePlugins.hpp>
#include <gtk/gtk.h>
#include <sys/utsname.h>
void NativeProbePlugin(const dotnative::PluginRegistrar &registrar) {
  auto window = registrar.NativeWindow();
  registrar.Channel("dotnative.plugins.nativeprobe")
      .Handle("read", [window](const dotnative::Value &,
                               std::shared_ptr<dotnative::Reply> reply) {
        if (!window || !GTK_IS_WINDOW(window)) {
          reply->Error("no_window", "Native window is unavailable");
          return;
        }
        utsname info{};
        if (uname(&info) != 0) {
          reply->Error("os_error", "uname failed");
          return;
        }
        reply->Success(std::string(info.sysname) + ": " + info.release);
      });
}