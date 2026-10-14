#import <AppKit/AppKit.h>
#import <QuartzCore/QuartzCore.h>
#include <stdint.h>
extern void dotnative_register_plugins(void) __attribute__((weak_import));
extern int32_t dotnative_macos_initialize(void *host);
extern int32_t dotnative_app_start(float width, float height);
extern int32_t dotnative_app_resize(float width, float height);
extern int32_t dotnative_app_tick(void);
extern int32_t dotnative_app_accessibility(int32_t motion,float scale,int32_t contrast);
extern int32_t dotnative_app_system_theme(int32_t theme);
extern int32_t dotnative_app_stop(void);
extern int32_t dotnative_app_activity(int32_t activity);
static void Check(int32_t status) { if(status) abort(); }
static void SyncAccessibility(void) {
    static int previousMotion=-1,previousContrast=-1;static float previousScale=-1;
    int motion=NSWorkspace.sharedWorkspace.accessibilityDisplayShouldReduceMotion,contrast=NSWorkspace.sharedWorkspace.accessibilityDisplayShouldIncreaseContrast; float scale=1;
    if(motion!=previousMotion || scale!=previousScale || contrast!=previousContrast) {Check(dotnative_app_accessibility(motion,scale,contrast));previousMotion=motion;previousScale=scale;previousContrast=contrast;}
}
static void SyncSystemTheme(void) {
    SyncAccessibility();
    static int32_t previous = 0;
    NSString *name = [NSApp.effectiveAppearance bestMatchFromAppearancesWithNames:@[NSAppearanceNameAqua, NSAppearanceNameDarkAqua]];
    int32_t theme = [name isEqualToString:NSAppearanceNameDarkAqua] ? 2 : 1;
    if (theme != previous) { Check(dotnative_app_system_theme(theme)); previous = theme; }
}

@interface MountView : NSView
@property BOOL started;
@end
@implementation MountView
- (BOOL)isFlipped { return YES; }
- (void)setFrameSize:(NSSize)size {
    [super setFrameSize:size];
    if(self.started) Check(dotnative_app_resize(size.width,size.height));
}
@end
@interface AppDelegate : NSObject <NSApplicationDelegate>
@property(strong) NSWindow *window;
@property(strong) NSTimer *timer;
@property(strong) CADisplayLink *displayLink API_AVAILABLE(macos(14.0));
@property CFTimeInterval lastFrame;
@end
@implementation AppDelegate
- (void)renderFrame {
    self.lastFrame = CACurrentMediaTime();
    SyncSystemTheme();
    Check(dotnative_app_tick());
}
- (void)displayFrame:(CADisplayLink *)link API_AVAILABLE(macos(14.0)) {
    [self renderFrame];
}
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    self.window = [[NSWindow alloc] initWithContentRect:NSMakeRect(0,0,520,620)
        styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable|NSWindowStyleMaskMiniaturizable|NSWindowStyleMaskResizable
        backing:NSBackingStoreBuffered defer:NO];
    self.window.title = @"Counter — DotNative";
    self.window.releasedWhenClosed = NO;
    MountView *host = [[MountView alloc] initWithFrame:NSMakeRect(0,0,520,620)];
    self.window.contentView = host;
    Check(dotnative_macos_initialize((__bridge void *)host));
    if (dotnative_register_plugins) dotnative_register_plugins();
    SyncSystemTheme();
    Check(dotnative_app_start(host.bounds.size.width,host.bounds.size.height));
    host.started = YES;
    Check(dotnative_app_activity(NSApp.isActive ? 0 : 1));
    NSTimeInterval interval = 1.0/60.0;
    if (@available(macOS 14.0, *)) {
        self.displayLink = [host displayLinkWithTarget:self selector:@selector(displayFrame:)];
        [self.displayLink addToRunLoop:NSRunLoop.mainRunLoop forMode:NSRunLoopCommonModes];
        interval = 0.1;
    }
    // Display links pause for invisible windows. Continue async/lifecycle work.
    self.timer = [NSTimer timerWithTimeInterval:interval repeats:YES block:^(NSTimer *timer) {
        if (@available(macOS 14.0, *)) {
            if (CACurrentMediaTime() - self.lastFrame < 0.1) return;
        }
        [self renderFrame];
    }];
    [[NSRunLoop mainRunLoop] addTimer:self.timer forMode:NSRunLoopCommonModes];
    [self.window center]; [self.window makeKeyAndOrderFront:nil];
    [NSApp activateIgnoringOtherApps:YES];
}
- (void)applicationDidBecomeActive:(NSNotification *)notification { Check(dotnative_app_activity(0)); }
- (void)applicationDidResignActive:(NSNotification *)notification { Check(dotnative_app_activity(1)); }
- (void)applicationDidHide:(NSNotification *)notification { Check(dotnative_app_activity(2)); }
- (void)applicationDidUnhide:(NSNotification *)notification { Check(dotnative_app_activity(NSApp.isActive ? 0 : 1)); }
- (BOOL)applicationShouldTerminateAfterLastWindowClosed:(NSApplication *)app {return YES;}
- (NSApplicationTerminateReply)applicationShouldTerminate:(NSApplication *)app {
    [self.timer invalidate];
    if (@available(macOS 14.0, *)) { [self.displayLink invalidate]; self.displayLink = nil; }
    ((MountView *)self.window.contentView).started = NO;
    int32_t status = dotnative_app_stop();
    if (status != 1) { Check(status); return NSTerminateNow; }
    self.timer = [NSTimer timerWithTimeInterval:0.01 repeats:YES block:^(NSTimer *timer) {
        int32_t next = dotnative_app_stop();
        if (next != 1) { [timer invalidate]; Check(next); [app replyToApplicationShouldTerminate:YES]; }
    }];
    [[NSRunLoop mainRunLoop] addTimer:self.timer forMode:NSRunLoopCommonModes];
    return NSTerminateLater;
}
@end
int main(int argc,char **argv) {
    @autoreleasepool {
        NSApplication *app=[NSApplication sharedApplication];
        [app setActivationPolicy:NSApplicationActivationPolicyRegular];

        // AppKit keyboard shortcuts are menu actions. Use terminate: so Quit
        // also follows applicationShouldTerminate and awaits managed cleanup.
        NSMenu *menuBar = [[NSMenu alloc] initWithTitle:@""];
        NSMenuItem *applicationItem = [[NSMenuItem alloc] initWithTitle:@"Counter" action:nil keyEquivalent:@""];
        [menuBar addItem:applicationItem];

        NSMenu *applicationMenu = [[NSMenu alloc] initWithTitle:@"Counter"];
        NSMenuItem *quit = [[NSMenuItem alloc] initWithTitle:@"Quit Counter"
            action:@selector(terminate:) keyEquivalent:@"q"];
        quit.target = app;
        quit.keyEquivalentModifierMask = NSEventModifierFlagCommand;
        [applicationMenu addItem:quit];
        applicationItem.submenu = applicationMenu;
        app.mainMenu = menuBar;

        __attribute__((objc_precise_lifetime)) AppDelegate *delegate=[AppDelegate new]; app.delegate=delegate;
        [app run];
    }
    return 0;
}
