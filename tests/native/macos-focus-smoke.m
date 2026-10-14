// Build core first: cargo build --manifest-path native/core/Cargo.toml
// clang -fobjc-arc -framework AppKit tests/native/macos-focus-smoke.m -L native/core/target/debug -lcore -Wl,-rpath,"$PWD/native/core/target/debug" -o /tmp/dotnative-focus-smoke
#import <AppKit/AppKit.h>
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
extern int32_t dotnative_macos_initialize(void *host);
extern void flush_commands(const uint8_t *buffer, size_t length);
extern size_t dotnative_poll_event(uint8_t *buffer, size_t capacity);
static void word(NSMutableData *data, uint32_t value) { [data appendBytes:&value length:4]; }
static void op(NSMutableData *data, uint8_t code, NSData *payload) {
    [data appendBytes:&code length:1]; word(data, (uint32_t)payload.length); [data appendData:payload];
}
static NSControl *find(NSView *view, NSString *name) {
    if ([NSStringFromClass(view.class) isEqualToString:name]) return (NSControl *)view;
    for (NSView *child in view.subviews) { NSControl *result=find(child,name); if(result) return result; }
    return nil;
}
static void drain(void) { uint8_t event[256]; while(dotnative_poll_event(event,sizeof(event))) {} }
static void expect(uint32_t id, uint32_t eventKind, float value) {
    uint8_t event[256]; BOOL found=NO;
    while(dotnative_poll_event(event,sizeof(event))) {
        uint32_t actualId,kind; float actualValue;
        memcpy(&actualId,event,4); memcpy(&kind,event+4,4); memcpy(&actualValue,event+8,4);
        if(actualId==id && kind==eventKind && actualValue==value) found=YES;
    }
    assert(found);
}
int main(void) {
    @autoreleasepool {
        [NSApplication sharedApplication];
        NSWindow *window=[[NSWindow alloc] initWithContentRect:NSMakeRect(0,0,400,300)
            styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
        window.releasedWhenClosed=NO;
        assert(dotnative_macos_initialize((__bridge void *)window.contentView)==0);
        NSMutableData *commands=[NSMutableData dataWithBytes:"DNTV\x01\x00\x10\x00" length:8];
        word(commands,0); word(commands,8);
        uint8_t kinds[]={1,12,6,7,5};
        for(uint32_t id=1;id<=5;id++) {
            NSMutableData *p=[NSMutableData data]; word(p,id); [p appendBytes:&kinds[id-1] length:1]; op(commands,1,p);
        }
        NSMutableData *p=[NSMutableData data];word(p,1);word(p,4);word(p,2);word(p,3);word(p,4);word(p,5);op(commands,4,p);
        p=[NSMutableData data];word(p,1);op(commands,5,p);
        float viewport[]={400,300};op(commands,7,[NSData dataWithBytes:viewport length:sizeof(viewport)]);
        uint32_t size=(uint32_t)commands.length;[commands replaceBytesInRange:NSMakeRange(8,4) withBytes:&size];
        flush_commands(commands.bytes,commands.length);
        NSArray *names=@[@"DotNativeMacRadioButton",@"DotNativeMacSwitch",@"DotNativeMacSlider"];
        for(NSUInteger index=0;index<names.count;index++) {
            NSControl *control=find(window.contentView,names[index]);assert(control);
            drain(); assert([window makeFirstResponder:control]); expect((uint32_t)index+2,4,1);
            assert([window makeFirstResponder:nil]);expect((uint32_t)index+2,4,0);
        }
        NSArray *hoverNames=[names arrayByAddingObject:@"DotNativeMacTextField"];
        for(NSUInteger index=0;index<hoverNames.count;index++) {
            NSControl *control=find(window.contentView,hoverNames[index]);assert(control);
            [control updateTrackingAreas]; NSUInteger count=control.trackingAreas.count;
            [control updateTrackingAreas]; assert(control.trackingAreas.count==count && count>0);
            NSEvent *enter=[NSEvent enterExitEventWithType:NSEventTypeMouseEntered location:NSZeroPoint modifierFlags:0 timestamp:0 windowNumber:window.windowNumber context:nil eventNumber:0 trackingNumber:0 userData:NULL];
            NSEvent *exit=[NSEvent enterExitEventWithType:NSEventTypeMouseExited location:NSZeroPoint modifierFlags:0 timestamp:0 windowNumber:window.windowNumber context:nil eventNumber:0 trackingNumber:0 userData:NULL];
            drain(); [control mouseEntered:enter];expect((uint32_t)index+2,8,1);
            [control mouseExited:exit];expect((uint32_t)index+2,8,0);
        }
        NSButton *radio=(NSButton *)find(window.contentView,@"DotNativeMacRadioButton");
        drain(); radio.cell.highlighted=YES;expect(2,7,1);
        radio.cell.highlighted=NO;expect(2,7,0);
        radio.state=NSControlStateValueOff;
        [radio performClick:nil];expect(2,3,1);assert(radio.state==NSControlStateValueOn);
        [radio performClick:nil];expect(2,3,1);assert(radio.state==NSControlStateValueOn);
        [window close]; drain();
        puts("PASS AppKit focus/blur, hover enter/exit, tracking-area replacement and radio highlight reset");
    }
    return 0;
}
