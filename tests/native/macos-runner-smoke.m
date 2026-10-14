// clang -fobjc-arc -framework AppKit -framework QuartzCore tests/native/macos-runner-smoke.m -o /tmp/dotnative-runner-smoke
// Run in a logged-in macOS session. Opens a temporary window and closes it automatically.
#define main dotnative_runner_main
#include "../../templates/DotNative.App/Platform/Mac/Runner/main.m"
#undef main
#include <assert.h>
#include <stdio.h>
static unsigned ticks, displayFrames;
static BOOL started, stopping, completed;
int32_t dotnative_macos_initialize(void *host) { return host ? 0 : -1; }
int32_t dotnative_app_start(float width,float height) { assert(width>0 && height>0); started=YES;return 0; }
int32_t dotnative_app_resize(float width,float height) { return 0; }
int32_t dotnative_app_tick(void) { assert(started && !stopping);++ticks;return 0; }
int32_t dotnative_app_accessibility(int32_t motion,float scale,int32_t contrast) { assert(scale>=.5 && scale<=5);return 0; }
int32_t dotnative_app_system_theme(int32_t theme) { return 0; }
int32_t dotnative_app_stop(void) { stopping=YES;return 0; }
int32_t dotnative_app_activity(int32_t activity) { return 0; }
@interface SmokeDelegate : AppDelegate
@end
@implementation SmokeDelegate
- (BOOL)applicationShouldTerminateAfterLastWindowClosed:(NSApplication *)app { return NO; }
- (NSApplicationTerminateReply)applicationShouldTerminate:(NSApplication *)app {
    if (!stopping) return NSTerminateCancel;
    return [super applicationShouldTerminate:app];
}
- (void)displayFrame:(CADisplayLink *)link API_AVAILABLE(macos(14.0)) {
    ++displayFrames;
    [super displayFrame:link];
}
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    [super applicationDidFinishLaunching:notification];
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, NSEC_PER_SEC), dispatch_get_main_queue(), ^{
        assert(ticks>0);
        if (@available(macOS 14.0,*)) { assert(displayFrames>0); }
        [self.window orderOut:nil];
        unsigned before=ticks;
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, NSEC_PER_SEC/2), dispatch_get_main_queue(), ^{
            assert(ticks>before); // hidden-window fallback must keep managed async work moving
            assert([super applicationShouldTerminate:NSApp]==NSTerminateNow);
            unsigned stopped=ticks;
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW, NSEC_PER_SEC/4), dispatch_get_main_queue(), ^{
                assert(ticks==stopped);
                completed=YES;
                fprintf(stderr,"PASS AppKit display frames, hidden-window progress and callback cleanup\n");
                [NSApp stop:nil];
                [NSApp postEvent:[NSEvent otherEventWithType:NSEventTypeApplicationDefined location:NSZeroPoint modifierFlags:0 timestamp:0 windowNumber:0 context:nil subtype:0 data1:0 data2:0] atStart:NO];
            });
        });
    });
}
@end
int main(void) {
    @autoreleasepool {
        NSApplication *app=NSApplication.sharedApplication;
        [app setActivationPolicy:NSApplicationActivationPolicyAccessory];
        __attribute__((objc_precise_lifetime)) SmokeDelegate *delegate=[SmokeDelegate new];
        app.delegate=delegate;
        [app run];
        assert(completed);
        [delegate.window close];
    }
    return 0;
}
