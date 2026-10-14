#ifndef DOTNATIVE_CORE_H
#define DOTNATIVE_CORE_H
#include <stddef.h>
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
/* OS main thread, synchronous borrow. buffer[0..len] must remain readable and
 * immutable through return. Errors are fatal in protocol v1 (void ABI).
 * Initialize the Rust platform measurer before sending Text/Button content. */
/* iOS only: a borrowed live UIView pointer, OS main thread, once per process. */
int32_t dotnative_ios_initialize(void *host);
void flush_commands(const uint8_t *buffer, size_t len);
#ifdef __cplusplus
}
#endif
#endif
