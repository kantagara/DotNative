#include <stddef.h>
#include <stdint.h>
int32_t dotnative_register_channel(uint32_t id,
                                   void (*handler)(const uint8_t *, intptr_t)) {
  return 0;
}
int32_t dotnative_plugin_reply(const uint8_t *bytes, intptr_t count) {
  return 0;
}
