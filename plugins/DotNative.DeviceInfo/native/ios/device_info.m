#import <Foundation/Foundation.h>
#include <stdint.h>

// Namespaced C symbol avoids collisions with independently authored plugins.
int32_t dotnative_deviceinfo_ios_major_version(void) {
    return (int32_t)NSProcessInfo.processInfo.operatingSystemVersion.majorVersion;
}
