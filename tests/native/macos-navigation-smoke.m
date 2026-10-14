// Build core, then clang -fobjc-arc -framework AppKit -framework QuartzCore
// tests/native/macos-navigation-smoke.m -L native/core/target/debug -lcore
// -Wl,-rpath,"$PWD/native/core/target/debug" -o /tmp/navigation-smoke
#import <AppKit/AppKit.h>
#import <QuartzCore/QuartzCore.h>
#import <objc/runtime.h>
#include <assert.h>
extern int32_t dotnative_macos_initialize(void *);
extern void flush_commands(const uint8_t *, size_t);
extern size_t dotnative_poll_event(uint8_t *, size_t);
// Exercise both OS accessibility policies without changing the user's settings.
static BOOL reducedMotion;
static BOOL testReducedMotion(id self, SEL cmd) { return reducedMotion; }
static NSMutableData *batch; static uint32_t count;
static void word(NSMutableData *data, uint32_t value) { [data appendBytes:&value length:4]; }
static void scalar(NSMutableData *data, float value) { [data appendBytes:&value length:4]; }
static void op(uint8_t code, NSData *payload) { [batch appendBytes:&code length:1]; word(batch,(uint32_t)payload.length); [batch appendData:payload]; count++; }
static NSMutableData *payload(uint32_t id) { NSMutableData *p=[NSMutableData data]; word(p,id); return p; }
static void begin(void) { batch=[NSMutableData dataWithBytes:"DNTV\x01\x00\x10\x00" length:8]; word(batch,0);word(batch,0);count=0; }
static void commit(void) { uint32_t size=(uint32_t)batch.length;[batch replaceBytesInRange:NSMakeRange(8,4) withBytes:&size];[batch replaceBytesInRange:NSMakeRange(12,4) withBytes:&count];flush_commands(batch.bytes,batch.length); }
static void children(uint32_t id, uint32_t first, uint32_t second) { NSMutableData *p=payload(id);word(p,second?2:1);word(p,first);if(second)word(p,second);op(4,p); }
static void depth(float value) { NSMutableData *p=payload(1);scalar(p,value);scalar(p,0);scalar(p,2);word(p,0);word(p,0);word(p,0);op(9,p); }
static void transition(uint8_t kind,uint8_t easing) { NSMutableData *p=payload(1);[p appendBytes:&kind length:1];scalar(p,1);[p appendBytes:&easing length:1];op(31,p); }
static void spin(double seconds) { NSDate *end=[NSDate dateWithTimeIntervalSinceNow:seconds];while(end.timeIntervalSinceNow>0)[NSRunLoop.currentRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:.005]]; }
static float curve(float t,int easing) { return easing==0?t:easing==2?1-powf(1-t,3):easing==3?t*t*t:t*t*(3-2*t); }
static BOOL shown(void) { uint8_t data[256];BOOL found=NO;while(dotnative_poll_event(data,sizeof(data))){uint32_t kind;memcpy(&kind,data+4,4);if(kind==5)found=YES;}return found; }
int main(void) { @autoreleasepool {
    [NSApplication sharedApplication];
    [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
    [NSApp finishLaunching];
    Method reducedMotionMethod=class_getInstanceMethod(NSWorkspace.class,@selector(accessibilityDisplayShouldReduceMotion));
    IMP originalReducedMotion=method_setImplementation(reducedMotionMethod,(IMP)testReducedMotion);
    NSWindow *window=[[NSWindow alloc] initWithContentRect:NSMakeRect(0,0,400,300) styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];window.releasedWhenClosed=NO;
    assert(dotnative_macos_initialize((__bridge void *)window.contentView)==0);
    begin(); for(uint32_t id=1;id<=5;id++){NSMutableData *p=payload(id);uint8_t kind=id==1?10:id<4?11:1;[p appendBytes:&kind length:1];op(1,p);}
    children(1,2,3);children(2,4,0);children(3,5,0);depth(1);op(5,payload(1));NSMutableData *p=[NSMutableData data];scalar(p,400);scalar(p,300);op(7,p);commit();
    [window center];[window makeKeyAndOrderFront:nil];[NSApp activateIgnoringOtherApps:YES];spin(.2);
    fprintf(stderr,"AppKit test window visible=%d occlusion=%lu active=%d reduceMotion=%d\n",window.visible,(unsigned long)window.occlusionState,NSApp.active,NSWorkspace.sharedWorkspace.accessibilityDisplayShouldReduceMotion);
    NSView *nav=window.contentView.subviews.firstObject;NSArray<NSView *> *pages=nav.subviews.copy;assert(pages.count==2);NSView *from=pages[0],*to=pages[1];assert(!from.hidden && to.hidden);
    for (uint32_t cycle=0; cycle<12; cycle++) {
        uint32_t mode=cycle%3;
        begin();NSMutableData *control=payload(1);scalar(control,1);scalar(control,0);scalar(control,2);word(control,mode);word(control,0);word(control,0);op(9,control);commit();
        NSString *expected=mode==1?NSAppearanceNameAqua:mode==2?NSAppearanceNameDarkAqua:nil;
        assert(expected ? [window.appearance.name isEqualToString:expected] : window.appearance==nil);
        assert(window.contentView.subviews.firstObject==nav);
    }
    puts("PASS AppKit: 12 Light/Dark/System changes synchronize native window chrome without remounting navigation");
    for(int kind=1;kind<=2;kind++)for(int easing=0;easing<4;easing++){
        shown();begin();transition(kind,easing);depth(2);commit();double start=CACurrentMediaTime();float previous=0;int intermediate=0;BOOL updated=NO;
        while(CACurrentMediaTime()-start<.90){spin(.025);CALayer *layer=to.layer.presentationLayer;assert(layer);float progress=kind==2?layer.opacity:1-layer.transform.m41/400.;double t=CACurrentMediaTime()-start;
            if(t>.4 && !updated){
                begin();NSMutableData *style=payload(3);scalar(style,0);uint8_t bg[]={40,120,200,255};[style appendBytes:bg length:4];scalar(style,0);word(style,0xff000000);scalar(style,17);op(3,style);commit();updated=YES;
            }
            if(from.hidden || to.hidden)fprintf(stderr,"early page hide: kind %d easing %d elapsed %.3f progress %.3f updated %d from %d to %d\n",kind,easing,t,progress,updated,from.hidden,to.hidden);
            assert(progress>=previous-.025);assert(!from.hidden && !to.hidden);assert(from.layer.presentationLayer.opacity>.99);assert(!shown());
            if(progress>.01 && progress<.99)intermediate++;
            if(fabsf(progress-curve(fminf(t,1),easing))>=.18)fprintf(stderr,"kind %d easing %d t %.3f progress %.3f expected %.3f pos %.2f target %.2f\n",kind,easing,t,progress,curve(fminf(t,1),easing),layer.position.x,to.layer.position.x);if(fabsf(progress-curve(fminf(t,1),easing))>=.18){abort();}previous=progress;
        }
        spin(.3);assert(shown());assert(from.hidden && !to.hidden);assert(intermediate>5);
        begin();transition(3,0);depth(1);commit();assert(shown());assert(!from.hidden && to.hidden);assert([nav.subviews containsObject:from] && [nav.subviews containsObject:to]);
    }
    reducedMotion=YES;begin();transition(1,0);depth(2);commit();assert(shown());assert(from.hidden && !to.hidden);
    method_setImplementation(reducedMotionMethod,originalReducedMotion);
    [window close];puts("PASS AppKit: native presentation progression for slide/fade, four easing curves, opaque outgoing page, completion ordering, retained identity, reduced-motion immediate completion");
}return 0;}
