extern void dotnative_open_activation(unsigned int kind, const void *text, unsigned long length);
#import <UIKit/UIKit.h>
#import <QuartzCore/QuartzCore.h>
#include <stdint.h>
#include <stdlib.h>

extern void dotnative_register_plugins(void) __attribute__((weak_import));
extern void dotnative_apns_registered(const unsigned char *, intptr_t) __attribute__((weak_import));
extern void dotnative_apns_failed(const char *) __attribute__((weak_import));
extern int32_t dotnative_ios_initialize(void *host);
extern int32_t dotnative_app_start(float width, float height);
extern int32_t dotnative_app_resize(float width, float height);
extern int32_t dotnative_app_insets(float top, float bottom, float keyboard);
extern int32_t dotnative_app_tick(void);
extern int32_t dotnative_app_accessibility(int32_t motion,float scale,int32_t contrast);
extern int32_t dotnative_app_stop(void);
extern int32_t dotnative_app_activity(int32_t activity);
extern int32_t dotnative_app_system_theme(int32_t theme);
static void RequireSuccess(int32_t status) { if (status != 0) abort(); }
static void SyncAccessibility(void) {
    static int previousMotion=-1,previousContrast=-1;static float previousScale=-1;
    int motion=UIAccessibilityIsReduceMotionEnabled(),contrast=UIAccessibilityDarkerSystemColorsEnabled(); float scale=fminf(5,fmaxf(.5,[UIFont preferredFontForTextStyle:UIFontTextStyleBody].pointSize/17.0));
    if(motion!=previousMotion || scale!=previousScale || contrast!=previousContrast) {RequireSuccess(dotnative_app_accessibility(motion,scale,contrast));previousMotion=motion;previousScale=scale;previousContrast=contrast;}
}
static void SyncSystemTheme(void) {
    SyncAccessibility();
    static int32_t previous = 0;
    int32_t theme = UIScreen.mainScreen.traitCollection.userInterfaceStyle == UIUserInterfaceStyleDark ? 2 : 1;
    if (theme != previous) { RequireSuccess(dotnative_app_system_theme(theme)); previous = theme; }
}

// This app lays out safe areas from WindowInsets. The canvas stays full-screen.
// Keep the renderer canvas full-sized so modal barriers cover system insets too.
@interface FniCanvasView : UIView
@end
@implementation FniCanvasView
- (UIEdgeInsets)safeAreaInsets { return UIEdgeInsetsZero; }
@end

@interface DotNativeController : UIViewController
@property(nonatomic, strong) UIView *mount;
@property(nonatomic, strong) CADisplayLink *displayLink;
@property(nonatomic) BOOL started;
@property(nonatomic) BOOL stopping;
@property(nonatomic) CGRect keyboardFrame;
@property(nonatomic) BOOL keyboardVisible;
@end
@implementation DotNativeController
- (void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = UIColor.whiteColor;
    self.mount = [[FniCanvasView alloc] initWithFrame:CGRectZero];
    [self.view addSubview:self.mount];
    self.keyboardVisible = NO;
    NSNotificationCenter *center = NSNotificationCenter.defaultCenter;
    [center addObserver:self selector:@selector(keyboardChanged:) name:UIKeyboardWillChangeFrameNotification object:nil];
    [center addObserver:self selector:@selector(keyboardHidden:) name:UIKeyboardWillHideNotification object:nil];
}
- (void)viewDidLayoutSubviews {
    [super viewDidLayoutSubviews];
    if (self.stopping) return;
    CGRect frame = self.view.bounds;
    // The canvas covers the whole window; application screens apply their own safe areas.
    frame.size.height = CGRectGetHeight(self.view.bounds);
    self.mount.frame = frame;
    CGSize size = self.mount.bounds.size;
    if (size.width <= 0 || size.height <= 0) return;
    if (!self.started) {
        RequireSuccess(dotnative_ios_initialize((__bridge void *)self.mount));
        if (dotnative_register_plugins) dotnative_register_plugins();
        SyncSystemTheme();
        NSString *dataDirectory = [NSSearchPathForDirectoriesInDomains(NSApplicationSupportDirectory, NSUserDomainMask, YES).firstObject stringByAppendingPathComponent:@"FaliNamIgrac"];
        setenv("FNI_DATA_DIRECTORY", dataDirectory.UTF8String, 1);
        RequireSuccess(dotnative_app_start((float)size.width, (float)size.height));
        self.started = YES;
        UIApplicationState state = UIApplication.sharedApplication.applicationState;
        RequireSuccess(dotnative_app_activity(state == UIApplicationStateActive ? 0 : (state == UIApplicationStateBackground ? 2 : 1)));
        self.displayLink = [CADisplayLink displayLinkWithTarget:self selector:@selector(tick:)];
        [self.displayLink addToRunLoop:NSRunLoop.mainRunLoop forMode:NSRunLoopCommonModes];
        NSLog(@"DotNative: C# startup accepted");
    } else {
        RequireSuccess(dotnative_app_resize((float)size.width, (float)size.height));
    }
    [self publishInsets];
}
- (void)keyboardChanged:(NSNotification *)note {
    self.keyboardFrame = [note.userInfo[UIKeyboardFrameEndUserInfoKey] CGRectValue];
    self.keyboardVisible = YES;
    [self publishInsets];
}
- (void)keyboardHidden:(NSNotification *)note {
    self.keyboardVisible = NO;
    [self publishInsets];
}
- (void)publishInsets {
    if (!self.started || self.stopping) return;
    UIEdgeInsets safe = self.view.safeAreaInsets;
    CGRect frame = self.mount.frame;
    CGFloat keyboard = 0;
    if (self.keyboardVisible) {
        CGRect converted = [self.view convertRect:self.keyboardFrame fromView:nil];
        keyboard = MAX(0, CGRectGetMaxY(frame) - CGRectGetMinY(converted));
    }
    RequireSuccess(dotnative_app_insets((float)safe.top, (float)safe.bottom, (float)keyboard));
}
- (void)tick:(CADisplayLink *)sender { SyncSystemTheme(); RequireSuccess(dotnative_app_tick()); }
- (void)stop {
    [self.displayLink invalidate]; self.displayLink = nil;
    if (self.started) { self.stopping = YES; self.started = NO; [self pollStop]; }
}
- (void)pollStop {
    int32_t status = dotnative_app_stop();
    if (status == 1) {
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 10 * NSEC_PER_MSEC), dispatch_get_main_queue(), ^{ [self pollStop]; });
    } else { RequireSuccess(status); }
}
@end

@interface AppDelegate : UIResponder <UIApplicationDelegate>
@property(nonatomic, strong) UIWindow *window;
@end
@implementation AppDelegate
- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)options {
    self.window = [[UIWindow alloc] initWithFrame:UIScreen.mainScreen.bounds];
    self.window.rootViewController = [DotNativeController new];
    [self.window makeKeyAndVisible];
    return YES;
}
- (void)application:(UIApplication *)application didRegisterForRemoteNotificationsWithDeviceToken:(NSData *)token {
    if (dotnative_apns_registered) dotnative_apns_registered(token.bytes, token.length);
}
- (void)application:(UIApplication *)application didFailToRegisterForRemoteNotificationsWithError:(NSError *)error {
    if (dotnative_apns_failed) dotnative_apns_failed(error.localizedDescription.UTF8String);
}
- (BOOL)application:(UIApplication *)application openURL:(NSURL *)url options:(NSDictionary *)options {
    NSData *data = [url.absoluteString dataUsingEncoding:NSUTF8StringEncoding];
    dotnative_open_activation(20, data.bytes, data.length);
    return YES;
}
- (BOOL)application:(UIApplication *)application continueUserActivity:(NSUserActivity *)activity restorationHandler:(void (^)(NSArray *))handler {
    if (![activity.activityType isEqualToString:NSUserActivityTypeBrowsingWeb] || !activity.webpageURL) return NO;
    NSData *data = [activity.webpageURL.absoluteString dataUsingEncoding:NSUTF8StringEncoding];
    dotnative_open_activation(20, data.bytes, data.length);
    return YES;
}
- (void)applicationDidBecomeActive:(UIApplication *)application { RequireSuccess(dotnative_app_activity(0)); }
- (void)applicationWillResignActive:(UIApplication *)application { RequireSuccess(dotnative_app_activity(1)); }
- (void)applicationDidEnterBackground:(UIApplication *)application { RequireSuccess(dotnative_app_activity(2)); }
- (void)applicationWillEnterForeground:(UIApplication *)application { RequireSuccess(dotnative_app_activity(1)); }
- (void)applicationWillTerminate:(UIApplication *)application {
    [(DotNativeController *)self.window.rootViewController stop];
}
@end
int main(int argc, char *argv[]) {
    @autoreleasepool { return UIApplicationMain(argc, argv, nil, NSStringFromClass(AppDelegate.class)); }
}
