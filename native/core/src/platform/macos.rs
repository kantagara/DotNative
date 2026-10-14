//! AppKit renderer. Flipped wrapper views give Taffy top-left logical coordinates.
//! Each leaf contains a real AppKit control; padding belongs to its wrapper.
mod navigation;
mod scroll;
#[cfg(feature = "dev-host")]
mod virtual_list_smoke;
#[cfg(feature = "dev-host")]
pub use virtual_list_smoke::run_virtual_list_smoke;
mod sidebar;
mod toolbar;
use crate::{AvailableSpace, Engine, Error, Kind, Measure, Node, Result, Size};
use objc2::{
    define_class, msg_send,
    rc::{autoreleasepool, Retained},
    sel, DefinedClass, MainThreadMarker, MainThreadOnly, Message,
};
use objc2_app_kit::{
    NSAppearanceCustomization, NSButton, NSColor, NSFont, NSLineBreakMode, NSTextField, NSView,
};
use objc2_core_foundation::{CGPoint, CGRect, CGSize};
use objc2_foundation::NSString;
use std::{
    cell::{Cell, RefCell},
    collections::{HashMap, HashSet},
    rc::Rc,
};

use objc2::AnyThread;
use objc2_app_kit::{NSImage, NSImageScaling, NSImageView, NSScrollView, NSSlider, NSSwitch};
use objc2_foundation::{NSData, NSNotification};
define_class!(
    #[unsafe(super(NSTextField))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeMacTextField"]
    #[ivars=MacInteractionState]
    struct NativeField;
    impl NativeField {
        #[unsafe(method(mouseEntered:))]
        fn entered(&self, _event:&objc2_app_kit::NSEvent) {crate::events::emit(self.ivars().id,8,1.,String::new());}
        #[unsafe(method(mouseExited:))]
        fn exited(&self, _event:&objc2_app_kit::NSEvent) {crate::events::emit(self.ivars().id,8,0.,String::new());}
        #[unsafe(method(updateTrackingAreas))]
        fn update_tracking(&self) {
            unsafe { let _:()=msg_send![super(self),updateTrackingAreas]; }
            update_tracking_area(self, self.ivars());
        }
        #[unsafe(method(controlTextDidChange:))]
        fn changed(&self,_notification:&NSNotification){crate::events::emit(self.ivars().id,2,0.,self.stringValue().to_string());}
        #[unsafe(method(controlTextDidBeginEditing:))]
        fn focused(&self,_notification:&NSNotification){crate::events::emit(self.ivars().id,4,1.,String::new());}
        #[unsafe(method(controlTextDidEndEditing:))]
        fn blurred(&self,_notification:&NSNotification){crate::events::emit(self.ivars().id,4,0.,String::new());}
    }
);
impl NativeField {
    fn make(id: u32, mtm: MainThreadMarker) -> Retained<NSTextField> {
        let allocated = Self::alloc(mtm).set_ivars(MacInteractionState {
            id,
            tracking: RefCell::new(None),
        });
        let field: Retained<Self> =
            unsafe { msg_send![super(allocated),initWithFrame:CGRect::ZERO] };
        // NSTextField's delegate is weak; the field implements these notification
        // selectors itself and cannot outlive its own callback receiver.
        unsafe {
            let _: () = msg_send![&field,setDelegate:&*field];
        }
        Retained::into_super(field)
    }
}
define_class!(
    #[unsafe(super(NSSwitch))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeMacSwitch"]
    #[ivars=MacInteractionState]
    struct NativeToggle;
    impl NativeToggle {
        #[unsafe(method(mouseEntered:))]
        fn entered(&self, _event:&objc2_app_kit::NSEvent) {crate::events::emit(self.ivars().id,8,1.,String::new());}
        #[unsafe(method(mouseExited:))]
        fn exited(&self, _event:&objc2_app_kit::NSEvent) {crate::events::emit(self.ivars().id,8,0.,String::new());}
        #[unsafe(method(updateTrackingAreas))]
        fn update_tracking(&self) {
            unsafe { let _:()=msg_send![super(self),updateTrackingAreas]; }
            update_tracking_area(self, self.ivars());
        }
        #[unsafe(method(becomeFirstResponder))]
        fn become_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), becomeFirstResponder] };
            if accepted { crate::events::emit(self.ivars().id, 4, 1., String::new()); }
            accepted
        }
        #[unsafe(method(resignFirstResponder))]
        fn resign_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), resignFirstResponder] };
            if accepted { crate::events::emit(self.ivars().id, 4, 0., String::new()); }
            accepted
        }
        #[unsafe(method(dotnativeValue:))]
        fn changed(&self,_sender:&NSSwitch){crate::events::emit(self.ivars().id,3,if self.state()!=0{1.}else{0.},String::new());}
    }
);
impl NativeToggle {
    fn make(id: u32, mtm: MainThreadMarker) -> Retained<NSSwitch> {
        let allocated = Self::alloc(mtm).set_ivars(MacInteractionState {
            id,
            tracking: RefCell::new(None),
        });
        let view: Retained<Self> =
            unsafe { msg_send![super(allocated),initWithFrame:CGRect::ZERO] };
        unsafe {
            view.setTarget(Some(&view));
            view.setAction(Some(sel!(dotnativeValue:)));
        }
        Retained::into_super(view)
    }
}
define_class!(
    #[unsafe(super(NSButton))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeMacRadioButton"]
    #[ivars=MacInteractionState]
    struct NativeRadio;
    impl NativeRadio {
        #[unsafe(method(mouseEntered:))]
        fn entered(&self, _event:&objc2_app_kit::NSEvent) {crate::events::emit(self.ivars().id,8,1.,String::new());}
        #[unsafe(method(mouseExited:))]
        fn exited(&self, _event:&objc2_app_kit::NSEvent) {crate::events::emit(self.ivars().id,8,0.,String::new());}
        #[unsafe(method(updateTrackingAreas))]
        fn update_tracking(&self) {
            unsafe { let _:()=msg_send![super(self),updateTrackingAreas]; }
            update_tracking_area(self, self.ivars());
        }
        #[unsafe(method(becomeFirstResponder))]
        fn become_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), becomeFirstResponder] };
            if accepted { crate::events::emit(self.ivars().id, 4, 1., String::new()); }
            accepted
        }
        #[unsafe(method(resignFirstResponder))]
        fn resign_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), resignFirstResponder] };
            if accepted { crate::events::emit(self.ivars().id, 4, 0., String::new()); }
            accepted
        }
        #[unsafe(method(dotnativeSelect:))]
        fn selected(&self, _sender: &NSButton) { crate::events::emit(self.ivars().id, 3, 1., String::new()); }
    }
);
impl NativeRadio {
    fn make(id: u32, mtm: MainThreadMarker) -> Retained<NSButton> {
        let allocated = Self::alloc(mtm).set_ivars(MacInteractionState {
            id,
            tracking: RefCell::new(None),
        });
        let view: Retained<Self> =
            unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] };
        unsafe {
            let cell: Retained<NativeButtonCell> = msg_send![super(NativeButtonCell::alloc(mtm).set_ivars(id)),initTextCell:&*NSString::from_str("")];
            view.setCell(Some(&cell));
            view.setButtonType(objc2_app_kit::NSButtonType::Radio);
            view.setTarget(Some(&view));
            view.setAction(Some(sel!(dotnativeSelect:)));
        }
        Retained::into_super(view)
    }
}
fn configure_radio(view: &NSButton, node: &Node) {
    view.setAttributedTitle(&attributed_text(node));
    view.setAlignment(text_alignment(node));
    view.setLineBreakMode(line_break(node));
    view.setFont(Some(&styled_font(node)));
    view.setState(if node.control.value == 1. { 1 } else { 0 });
    view.setContentTintColor(Some(&color(node.style.foreground)));
}
define_class!(
    #[unsafe(super(NSSlider))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeMacSlider"]
    #[ivars=MacInteractionState]
    struct NativeRange;
    impl NativeRange {
        #[unsafe(method(mouseEntered:))]
        fn entered(&self, _event:&objc2_app_kit::NSEvent) {crate::events::emit(self.ivars().id,8,1.,String::new());}
        #[unsafe(method(mouseExited:))]
        fn exited(&self, _event:&objc2_app_kit::NSEvent) {crate::events::emit(self.ivars().id,8,0.,String::new());}
        #[unsafe(method(updateTrackingAreas))]
        fn update_tracking(&self) {
            unsafe { let _:()=msg_send![super(self),updateTrackingAreas]; }
            update_tracking_area(self, self.ivars());
        }
        #[unsafe(method(becomeFirstResponder))]
        fn become_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), becomeFirstResponder] };
            if accepted { crate::events::emit(self.ivars().id, 4, 1., String::new()); }
            accepted
        }
        #[unsafe(method(resignFirstResponder))]
        fn resign_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), resignFirstResponder] };
            if accepted { crate::events::emit(self.ivars().id, 4, 0., String::new()); }
            accepted
        }
        #[unsafe(method(dotnativeValue:))]
        fn changed(&self,_sender:&NSSlider){crate::events::emit(self.ivars().id,3,self.doubleValue() as f32,String::new());}
    }
);
impl NativeRange {
    fn make(id: u32, mtm: MainThreadMarker) -> Retained<NSSlider> {
        let allocated = Self::alloc(mtm).set_ivars(MacInteractionState {
            id,
            tracking: RefCell::new(None),
        });
        let view: Retained<Self> =
            unsafe { msg_send![super(allocated),initWithFrame:CGRect::ZERO] };
        unsafe {
            view.setTarget(Some(&view));
            view.setAction(Some(sel!(dotnativeValue:)));
        }
        Retained::into_super(view)
    }
}
pub(super) fn decoded_image(node: &Node) -> Result<Retained<NSImage>> {
    let image = if node.kind == Kind::Icon {
        let name = match &*node.text {
            "star" => "star.fill",
            "check" => "checkmark.circle.fill",
            "info" => "info.circle",
            _ => return Err(Error::Measurement("unknown icon")),
        };
        NSImage::imageWithSystemSymbolName_accessibilityDescription(&NSString::from_str(name), None)
    } else {
        NSImage::initWithData(NSImage::alloc(), &NSData::with_bytes(&node.image))
    };
    let image = image.ok_or(Error::Measurement("AppKit could not decode image"))?;
    if !node.background_image.is_empty()
        && unsafe {
            image
                .CGImageForProposedRect_context_hints(std::ptr::null_mut(), None, None)
                .is_none()
        }
    {
        return Err(Error::Measurement(
            "AppKit could not rasterize background image",
        ));
    }
    if node.kind == Kind::Image {
        image.setTemplate(node.image_tint.is_some());
    }
    Ok(image)
}
fn configure_field(field: &NSTextField, node: &Node) {
    // The outer view owns the DSL background, padding and corner radius.
    // Disable AppKit's separate bezel/fill, which follows the OS appearance
    // and can otherwise produce a dark inset inside a light themed wrapper.
    field.setBezeled(false);
    field.setBordered(false);
    field.setDrawsBackground(false);
    if field.stringValue().to_string() != node.text.as_ref() {
        // Avoid replacing marked text during native IME composition.
        let editor = field.currentEditor();
        let marked = editor
            .as_ref()
            .is_some_and(|e| unsafe { msg_send![e, hasMarkedText] });
        if !marked {
            field.setStringValue(&NSString::from_str(&node.text));
        }
    }
    field.setAlignment(text_alignment(node));
    field.setLineBreakMode(line_break(node));
    field.setMaximumNumberOfLines(max_lines(node));
    field.setPlaceholderString(Some(&NSString::from_str(&node.control.placeholder)));
    field.setFont(Some(&styled_font(node)));
    field.setTextColor(Some(&color(node.style.foreground)));
}

struct FlippedViewState {
    hit_test: Rc<Cell<bool>>,
    transform: Cell<Option<objc2_quartz_core::CATransform3D>>,
}
define_class!(
    #[unsafe(super(NSView))]
    #[thread_kind = MainThreadOnly]
    #[name = "DotNativeFlippedView"]
    #[ivars=FlippedViewState]
    struct FlippedView;
    impl FlippedView {
        #[unsafe(method_id(hitTest:))]
        fn hit_test(&self, point: CGPoint) -> Option<Retained<NSView>> {
            if self.ivars().hit_test.get() { Some(Retained::into_super(self.retain())) }
            else { unsafe { msg_send![super(self), hitTest: point] } }
        }
        #[unsafe(method(isFlipped))]
        fn flipped(&self) -> bool { true }
        #[unsafe(method(wantsUpdateLayer))]
        fn wants_update_layer(&self) -> bool { true }
        #[unsafe(method(updateLayer))]
        fn update_layer(&self) { self.restore_transform(); }
        #[unsafe(method(layout))]
        fn layout(&self) {
            unsafe { let _: () = msg_send![super(self), layout]; }
            self.restore_transform();
        }
    }
);
impl FlippedView {
    fn set_visual_transform(&self, transform: objc2_quartz_core::CATransform3D) {
        self.ivars().transform.set(Some(transform));
        self.restore_transform();
        // AppKit may recreate/reset a backing layer after the command batch.
        // Reapply our visual transform in its layer update lifecycle as well.
        self.setNeedsDisplay(true);
    }

    fn restore_transform(&self) {
        if let (Some(layer), Some(transform)) = (self.layer(), self.ivars().transform.get()) {
            if layer.transform() != transform {
                // AppKit can call this outside our command-batch transaction.
                // Restore the current frame without starting an implicit animation.
                objc2_quartz_core::CATransaction::begin();
                objc2_quartz_core::CATransaction::setDisableActions(true);
                layer.setTransform(transform);
                objc2_quartz_core::CATransaction::commit();
            }
        }
    }

    fn make(mtm: MainThreadMarker) -> Retained<NSView> {
        let view: Retained<Self> = unsafe {
            msg_send![super(Self::alloc(mtm).set_ivars(FlippedViewState {
                hit_test: Rc::new(Cell::new(false)),
                transform: Cell::new(None),
            })), initWithFrame: CGRect::ZERO]
        };
        Retained::into_super(view)
    }
}
define_class!(
    #[unsafe(super(objc2_app_kit::NSButtonCell))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeMacButtonCell"]
    #[ivars=u32]
    struct NativeButtonCell;
    impl NativeButtonCell {
        #[unsafe(method(drawImage:withFrame:inView:))]
        fn draw_image(&self, image: &NSImage, frame: CGRect, view: &NSView) {
            unsafe { let _: () = msg_send![super(self), drawImage: image, withFrame: frame, inView: view]; }
            // AppKit's unselected radio fill can be almost indistinguishable
            // from a plain surface. Keep its native glyph and add a clear rim.
            if self.state() == 0 && view.downcast_ref::<NativeRadio>().is_some() {
                let diameter = frame.size.width.min(frame.size.height) - 2.;
                if diameter > 0. {
                    let ring = objc2_app_kit::NSBezierPath::bezierPathWithOvalInRect(CGRect::new(
                        CGPoint::new(
                            frame.origin.x + (frame.size.width - diameter) / 2.,
                            frame.origin.y + (frame.size.height - diameter) / 2.,
                        ),
                        CGSize::new(diameter, diameter),
                    ));
                    NSColor::labelColor()
                        .colorWithAlphaComponent(if self.isEnabled() { 0.6 } else { 0.25 })
                        .setStroke();
                    ring.setLineWidth(1.);
                    ring.stroke();
                }
            }
        }
        #[unsafe(method(setHighlighted:))]
        fn set_highlighted(&self,value:bool) {
            let changed=self.isHighlighted()!=value;
            unsafe{let _:()=msg_send![super(self),setHighlighted:value];}
            if changed {crate::events::emit(*self.ivars(),7,if value {1.}else{0.},String::new());}
        }
    }
);
fn update_tracking_area(view: &NSView, state: &MacInteractionState) {
    if let Some(old) = state.tracking.borrow_mut().take() {
        view.removeTrackingArea(&old);
    }
    use objc2_app_kit::{NSTrackingArea, NSTrackingAreaOptions as O};
    let area = unsafe {
        NSTrackingArea::initWithRect_options_owner_userInfo(
            NSTrackingArea::alloc(),
            CGRect::ZERO,
            O::MouseEnteredAndExited | O::ActiveInKeyWindow | O::InVisibleRect,
            Some(view),
            None,
        )
    };
    view.addTrackingArea(&area);
    *state.tracking.borrow_mut() = Some(area);
}
struct MacInteractionState {
    id: u32,
    tracking: RefCell<Option<Retained<objc2_app_kit::NSTrackingArea>>>,
}
define_class!(
    #[unsafe(super(NSButton))]
    #[thread_kind = MainThreadOnly]
    #[name = "DotNativeMacButton"]
    #[ivars = MacInteractionState]
    struct NativeButton;
    impl NativeButton {
        #[unsafe(method(becomeFirstResponder))]
        fn become_first_responder(&self) -> bool { let ok:bool=unsafe{msg_send![super(self),becomeFirstResponder]}; if ok {crate::events::emit(self.ivars().id,4,1.,String::new());} ok }
        #[unsafe(method(resignFirstResponder))]
        fn resign_first_responder(&self) -> bool { let ok:bool=unsafe{msg_send![super(self),resignFirstResponder]}; if ok {crate::events::emit(self.ivars().id,4,0.,String::new());} ok }
        #[unsafe(method(mouseEntered:))]
        fn entered(&self, _event:&objc2_app_kit::NSEvent) {crate::events::emit(self.ivars().id,8,1.,String::new());}
        #[unsafe(method(mouseExited:))]
        fn exited(&self, _event:&objc2_app_kit::NSEvent) {crate::events::emit(self.ivars().id,8,0.,String::new());}
        #[unsafe(method(updateTrackingAreas))]
        fn update_tracking(&self) {
            unsafe { let _:()=msg_send![super(self),updateTrackingAreas]; }
            update_tracking_area(self, self.ivars());
        }

        #[unsafe(method(dotnativeClick:))]
        fn click(&self, _sender: &NSButton) { crate::events::enqueue(self.ivars().id); }
    }
);
impl NativeButton {
    fn make(id: u32, mtm: MainThreadMarker) -> Retained<NSButton> {
        let allocated = Self::alloc(mtm).set_ivars(MacInteractionState {
            id,
            tracking: RefCell::new(None),
        });
        // The unretained target is the control itself; no external target lifetime.
        let button: Retained<Self> =
            unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] };
        let cell: Retained<NativeButtonCell> = unsafe {
            msg_send![super(NativeButtonCell::alloc(mtm).set_ivars(id)),initTextCell:&*NSString::from_str("")]
        };
        button.setCell(Some(&cell));
        unsafe {
            button.setTarget(Some(&button));
            button.setAction(Some(sel!(dotnativeClick:)));
        }
        Retained::into_super(button)
    }
}
fn attributed_text(node: &Node) -> Retained<objc2_foundation::NSMutableAttributedString> {
    use objc2::AnyThread;
    use objc2_app_kit::{
        NSFontAttributeName, NSForegroundColorAttributeName, NSKernAttributeName,
        NSMutableParagraphStyle, NSParagraphStyleAttributeName, NSStrikethroughStyleAttributeName,
        NSUnderlineStyleAttributeName,
    };
    use objc2_foundation::{NSMutableAttributedString, NSNumber, NSRange};
    let text = NSString::from_str(&node.text);
    let result =
        NSMutableAttributedString::initWithString(NSMutableAttributedString::alloc(), &text);
    let range = NSRange::new(0, text.length());
    let paragraph = NSMutableParagraphStyle::new();
    paragraph.setAlignment(text_alignment(node));
    paragraph.setLineBreakMode(line_break(node));
    paragraph.setMinimumLineHeight(node.typography.line_height as _);
    paragraph.setMaximumLineHeight(node.typography.line_height as _);
    // Native attribute keys require these exact value classes and a UTF-16 range.
    unsafe {
        result.addAttribute_value_range(NSFontAttributeName, &styled_font(node), range);
        result.addAttribute_value_range(
            NSForegroundColorAttributeName,
            &color(node.style.foreground),
            range,
        );
        result.addAttribute_value_range(
            NSKernAttributeName,
            &NSNumber::new_f32(node.typography.letter_spacing),
            range,
        );
        result.addAttribute_value_range(NSParagraphStyleAttributeName, &paragraph, range);
        result.addAttribute_value_range(
            NSUnderlineStyleAttributeName,
            &NSNumber::new_i32((node.typography.decoration & 1 != 0) as i32),
            range,
        );
        result.addAttribute_value_range(
            NSStrikethroughStyleAttributeName,
            &NSNumber::new_i32((node.typography.decoration & 2 != 0) as i32),
            range,
        );
    }
    let mut offset = 0;
    for run in node.text_runs.iter() {
        let styled = node.run_style(run);
        let length = run.text.encode_utf16().count();
        let range = NSRange::new(offset, length);
        offset += length;
        unsafe {
            result.addAttribute_value_range(NSFontAttributeName, &styled_font(&styled), range);
            result.addAttribute_value_range(
                NSForegroundColorAttributeName,
                &color(styled.style.foreground),
                range,
            );
            result.addAttribute_value_range(
                NSUnderlineStyleAttributeName,
                &NSNumber::new_i32((styled.typography.decoration & 1 != 0) as i32),
                range,
            );
            result.addAttribute_value_range(
                NSStrikethroughStyleAttributeName,
                &NSNumber::new_i32((styled.typography.decoration & 2 != 0) as i32),
                range,
            );
        }
    }
    result
}

fn styled_font(node: &Node) -> Retained<NSFont> {
    let weight = [-0.8, -0.6, -0.4, 0., 0.23, 0.3, 0.4, 0.56, 0.62]
        [(node.typography.weight / 100 - 1) as usize];
    let size = node.style.font_size as _;
    let base = NSFont::systemFontOfSize_weight(size, weight);
    // Same PostScript lookup as iOS: the Windows family name is not the AppKit family.
    let mut descriptor = if let Some(face) = node
        .font_asset
        .as_ref()
        .and_then(|asset| NSFont::fontWithName_size(&NSString::from_str(&asset.face), size))
    {
        face.fontDescriptor()
    } else {
        let mut descriptor = base.fontDescriptor();
        if !node.typography.family.is_empty() {
            // A system descriptor retains a concrete font name; changing only its
            // family can still resolve to the system font. Start with family + traits.
            let family = NSString::from_str(&node.typography.family);
            descriptor = unsafe {
                let weight = objc2_foundation::NSNumber::new_f64(weight);
                let traits = objc2_foundation::NSDictionary::from_slices(
                    &[objc2_app_kit::NSFontWeightTrait],
                    &[&*weight as &objc2::runtime::AnyObject],
                );
                let keys = [
                    objc2_app_kit::NSFontFamilyAttribute,
                    objc2_app_kit::NSFontTraitsAttribute,
                ];
                let values: [&objc2::runtime::AnyObject; 2] = [&*family, &*traits];
                objc2_app_kit::NSFontDescriptor::fontDescriptorWithFontAttributes(Some(
                    &objc2_foundation::NSDictionary::from_slices(&keys, &values),
                ))
            };
        }
        descriptor
    };
    if node.typography.italic {
        let traits = descriptor.symbolicTraits()
            | objc2_app_kit::NSFontDescriptorSymbolicTraits::TraitItalic;
        descriptor = descriptor.fontDescriptorWithSymbolicTraits(traits);
    }
    if !node.font_axes.is_empty() {
        descriptor = unsafe {
            descriptor
                .fontDescriptorByAddingAttributes(&super::apple_fonts::variation_attributes(node))
        };
    }
    NSFont::fontWithDescriptor_size(&descriptor, size).unwrap_or(base)
}
fn line_break(node: &Node) -> NSLineBreakMode {
    match node.typography.overflow {
        1 => NSLineBreakMode::ByWordWrapping,
        2 => NSLineBreakMode::ByClipping,
        3 => NSLineBreakMode::ByTruncatingHead,
        4 => NSLineBreakMode::ByTruncatingMiddle,
        5 => NSLineBreakMode::ByTruncatingTail,
        _ if node.kind == Kind::Text => NSLineBreakMode::ByWordWrapping,
        _ => NSLineBreakMode::ByTruncatingTail,
    }
}
fn text_alignment(node: &Node) -> objc2_app_kit::NSTextAlignment {
    use objc2_app_kit::NSTextAlignment;
    match node.text_alignment() {
        1 => NSTextAlignment::Center,
        2 => NSTextAlignment::Right,
        3 => NSTextAlignment::Justified,
        _ => NSTextAlignment::Left,
    }
}
fn max_lines(node: &Node) -> isize {
    if node.typography.max_lines >= 0 {
        node.typography.max_lines as isize
    } else if node.kind == Kind::Text {
        0
    } else {
        1
    }
}

fn apply_gradient(parent: &objc2_quartz_core::CALayer, node: &Node) {
    use objc2_foundation::{NSArray, NSNumber};
    use objc2_quartz_core::{
        kCAGradientLayerAxial, kCAGradientLayerConic, kCAGradientLayerRadial, CAGradientLayer,
    };
    // CALayer hierarchy is confined to the UI thread; these are framework-owned decoration layers.
    let existing = unsafe { parent.sublayers() }.and_then(|layers| {
        layers.iter().find(|l| {
            l.name()
                .is_some_and(|name| name.to_string() == "DotNativeGradient")
        })
    });
    let Some(g) = &node.gradient else {
        if let Some(layer) = existing {
            layer.removeFromSuperlayer();
        }
        return;
    };
    let layer = existing
        .and_then(|layer| layer.downcast::<CAGradientLayer>().ok())
        .unwrap_or_else(|| {
            let layer = CAGradientLayer::new();
            layer.setName(Some(&NSString::from_str("DotNativeGradient")));
            parent.insertSublayer_atIndex(&layer, 0);
            layer
        });
    let bounds = parent.bounds();
    layer.setFrame(bounds);
    layer.setCornerRadius(node.style.radius as _);
    layer.setMasksToBounds(true);
    super::apple_box::clip(&layer, node, true);
    let colors: Vec<_> = g.stops.iter().map(|(_, c)| color(*c).CGColor()).collect();
    let objects: Vec<&objc2::runtime::AnyObject> = colors
        .iter()
        .map(|c| unsafe { &*((&**c as *const _) as *const objc2::runtime::AnyObject) })
        .collect();
    let locations: Vec<_> = g
        .stops
        .iter()
        .map(|(offset, _)| NSNumber::new_f32(*offset))
        .collect();
    unsafe {
        layer.setColors(Some(&NSArray::from_slice(&objects)));
    }
    layer.setLocations(Some(&NSArray::from_retained_slice(&locations)));
    layer.setStartPoint(CGPoint::new(g.start[0] as _, g.start[1] as _));
    let end = if g.kind == 2 {
        let radius = bounds.size.width.min(bounds.size.height) * g.end[0] as f64;
        CGPoint::new(
            g.start[0] as f64 + radius / bounds.size.width.max(1.),
            g.start[1] as f64 + radius / bounds.size.height.max(1.),
        )
    } else if g.kind == 3 {
        CGPoint::new(g.start[0] as f64, g.start[1] as f64 - 1.)
    } else {
        CGPoint::new(g.end[0] as _, g.end[1] as _)
    };
    layer.setEndPoint(end);
    unsafe {
        layer.setType(match g.kind {
            2 => kCAGradientLayerRadial,
            3 => kCAGradientLayerConic,
            _ => kCAGradientLayerAxial,
        });
    }
}

fn apply_visual(
    layer: &objc2_quartz_core::CALayer,
    node: &Node,
) -> objc2_quartz_core::CATransform3D {
    let v = node.visual;
    layer.setOpacity(v.opacity);
    layer.setBorderWidth(v.border_width as _);
    layer.setBorderColor(Some(&color(v.border_color).CGColor()));
    layer.setShadowColor(Some(&color(v.shadow_color).CGColor()));
    layer.setShadowOpacity(1.);
    layer.setShadowRadius(v.shadow_blur as _);
    layer.setShadowOffset(CGSize::new(v.shadow_x as _, v.shadow_y as _));
    layer.setMasksToBounds(v.clip && node.kind != Kind::NavigationPage);
    let transform = objc2_quartz_core::CATransform3D::new_translation(
        v.translate_x as _,
        v.translate_y as _,
        0.,
    )
    .rotate((v.rotation as f64).to_radians(), 0., 0., 1.)
    .scale(v.scale_x as _, v.scale_y as _, 1.);
    let p = node.projection;
    let projection = objc2_quartz_core::CATransform3D {
        m11: p[0] as _,
        m12: p[1] as _,
        m13: p[2] as _,
        m14: p[3] as _,
        m21: p[4] as _,
        m22: p[5] as _,
        m23: p[6] as _,
        m24: p[7] as _,
        m31: p[8] as _,
        m32: p[9] as _,
        m33: p[10] as _,
        m34: p[11] as _,
        m41: p[12] as _,
        m42: p[13] as _,
        m43: p[14] as _,
        m44: p[15] as _,
    };
    // AppKit uses a top-left anchor for backing layers. The public transform
    // contract uses the box center; conjugate without changing AppKit's anchor.
    let bounds = layer.bounds();
    let anchor = layer.anchorPoint();
    let pivot_x = bounds.size.width * (0.5 - anchor.x);
    let pivot_y = bounds.size.height * (0.5 - anchor.y);
    centered_transform(transform.concat(projection), pivot_x, pivot_y)
}

fn centered_transform(
    transform: objc2_quartz_core::CATransform3D,
    x: f64,
    y: f64,
) -> objc2_quartz_core::CATransform3D {
    use objc2_quartz_core::CATransform3D;
    CATransform3D::new_translation(-x, -y, 0.)
        .concat(transform)
        .concat(CATransform3D::new_translation(x, y, 0.))
}

#[cfg(test)]
mod transform_tests {
    use super::centered_transform;
    use objc2_quartz_core::CATransform3D;

    fn project(m: CATransform3D, x: f64, y: f64) -> (f64, f64) {
        let w = x * m.m14 + y * m.m24 + m.m44;
        (
            (x * m.m11 + y * m.m21 + m.m41) / w,
            (x * m.m12 + y * m.m22 + m.m42) / w,
        )
    }

    #[test]
    fn backing_layer_projection_uses_the_box_center() {
        let mut projection = CATransform3D::new_scale(1., 0.85, 1.);
        projection.m24 = -0.0013;
        projection.m44 = 1.027;
        projection.m43 = -9.;
        let centered = centered_transform(projection, 43., 18.);
        for (x, y) in [(0., 0.), (43., 18.), (86., 36.), (23., 9.)] {
            let relative = project(projection, x - 43., y - 18.);
            let actual = project(centered, x, y);
            assert!((actual.0 - (relative.0 + 43.)).abs() < 1e-9);
            assert!((actual.1 - (relative.1 + 18.)).abs() < 1e-9);
        }
    }

    #[test]
    fn centering_preserves_translation_and_center_anchored_layers() {
        let translation = CATransform3D::new_translation(5., -8., 0.);
        assert_eq!(
            project(centered_transform(translation, 43., 18.), 43., 18.),
            (48., 10.)
        );
        assert_eq!(centered_transform(translation, 0., 0.), translation);
    }
}

fn color(v: [u8; 4]) -> Retained<NSColor> {
    NSColor::colorWithSRGBRed_green_blue_alpha(
        v[0] as f64 / 255.,
        v[1] as f64 / 255.,
        v[2] as f64 / 255.,
        v[3] as f64 / 255.,
    )
}
fn label(mtm: MainThreadMarker) -> Retained<NSTextField> {
    let label = NSTextField::labelWithString(&NSString::from_str(""), mtm);
    label.setMaximumNumberOfLines(0);
    label.setLineBreakMode(NSLineBreakMode::ByWordWrapping);
    label
}
fn configure_label(view: &NSTextField, n: &Node) {
    view.setStringValue(&NSString::from_str(&n.text));
    view.setAlignment(text_alignment(n));
    view.setMaximumNumberOfLines(max_lines(n));
    view.setLineBreakMode(line_break(n));
    view.setFont(Some(&styled_font(n)));
    view.setTextColor(Some(&color(n.style.foreground)));
    view.setAttributedStringValue(&attributed_text(n));
}
fn configure_button(view: &NSButton, n: &Node) {
    view.setAttributedTitle(&attributed_text(n));
    view.setAlignment(text_alignment(n));
    view.setLineBreakMode(line_break(n));
    view.setFont(Some(&styled_font(n)));
    view.setBordered(false);
    view.setContentTintColor(Some(&color(n.style.foreground)));
}
unsafe extern "C-unwind" fn compare_paint_order(
    a: std::ptr::NonNull<NSView>,
    b: std::ptr::NonNull<NSView>,
    context: *mut std::ffi::c_void,
) -> objc2_foundation::NSComparisonResult {
    let order = unsafe { &*context.cast::<HashMap<usize, usize>>() };
    match order
        .get(&(a.as_ptr() as usize))
        .unwrap_or(&0)
        .cmp(order.get(&(b.as_ptr() as usize)).unwrap_or(&0))
    {
        std::cmp::Ordering::Less => objc2_foundation::NSComparisonResult::Ascending,
        std::cmp::Ordering::Greater => objc2_foundation::NSComparisonResult::Descending,
        _ => objc2_foundation::NSComparisonResult::Same,
    }
}
struct Entry {
    cells: super::cell_pool::CellPool<Retained<NSView>>,
    sidebar: Option<sidebar::Sidebar>,
    background: Option<super::apple_background::Background>,
    view: Retained<NSView>,
    label: Option<Retained<NSTextField>>,
    button: Option<Retained<NSButton>>,
    extra: Option<Retained<NSView>>,
    document: Option<Retained<NSView>>,
    hosted_type: String,
}
impl Entry {
    fn new(id: u32, kind: Kind, mtm: MainThreadMarker) -> Self {
        let view = FlippedView::make(mtm);
        let sidebar = (kind == Kind::DesktopNavigation).then(|| sidebar::Sidebar::new(id, mtm));
        if let Some(sidebar) = &sidebar {
            view.addSubview(&sidebar.controller.view());
        }
        view.setWantsLayer(true);
        let label = (kind == Kind::Text).then(|| label(mtm));
        let button = (kind == Kind::Button).then(|| NativeButton::make(id, mtm));
        if let Some(v) = &label {
            view.addSubview(v);
        }
        if let Some(v) = &button {
            view.addSubview(v);
        }
        let extra = match kind {
            Kind::RadioButton => Some(Retained::into_super(Retained::into_super(
                NativeRadio::make(id, mtm),
            ))),
            Kind::TextField => Some(Retained::into_super(Retained::into_super(
                NativeField::make(id, mtm),
            ))),
            Kind::Switch => Some(Retained::into_super(Retained::into_super(
                NativeToggle::make(id, mtm),
            ))),
            Kind::Slider => Some(Retained::into_super(Retained::into_super(
                NativeRange::make(id, mtm),
            ))),
            Kind::Image | Kind::Icon => Some(Retained::into_super(Retained::into_super(
                NSImageView::initWithFrame(NSImageView::alloc(mtm), CGRect::ZERO),
            ))),
            Kind::ScrollView => Some(Retained::into_super(Retained::into_super(
                scroll::NativeScroll::new(id, mtm),
            ))),
            _ => None,
        };
        let document = if let Some(sidebar) = &sidebar {
            Some(sidebar.content.clone())
        } else if kind == Kind::ScrollView {
            let doc = FlippedView::make(mtm);
            let scroll = extra
                .as_ref()
                .unwrap()
                .downcast_ref::<NSScrollView>()
                .unwrap();
            scroll.setDocumentView(Some(&doc));
            scroll.setHasVerticalScroller(true);
            scroll.setDrawsBackground(false);
            Some(doc)
        } else {
            None
        };
        if let Some(v) = &extra {
            view.addSubview(v);
        }
        Self {
            cells: Default::default(),
            sidebar,
            background: None,
            view,
            label,
            button,
            extra,
            document,
            hosted_type: String::new(),
        }
    }

    fn release_platform_view(&mut self, id: u32) {
        if self.hosted_type.is_empty() {
            return;
        }
        if let Some(child) = self.extra.take() {
            crate::platform_view::detach(id, &self.hosted_type, Retained::as_ptr(&child).cast());
            child.removeFromSuperview();
        }
        self.hosted_type.clear();
    }
}
struct NoAnimations;
impl NoAnimations {
    fn new() -> Self {
        objc2_quartz_core::CATransaction::begin();
        objc2_quartz_core::CATransaction::setDisableActions(true);
        Self
    }
}
impl Drop for NoAnimations {
    fn drop(&mut self) {
        objc2_quartz_core::CATransaction::commit();
    }
}
struct Registry {
    toolbar: Option<Retained<toolbar::NativeToolbar>>,
    navigation: HashMap<u32, navigation::State>,
    host: Retained<NSView>,
    entries: HashMap<u32, Entry>,
    applied: im::HashMap<u32, std::sync::Arc<Node>>,
    frames: HashMap<u32, crate::engine::Frame>,
    root: u32,
    mtm: MainThreadMarker,
}
impl Registry {
    fn sync(&mut self, engine: &Engine) -> Result<()> {
        let frames: Vec<_> = engine
            .nodes()
            .map(|(id, _)| engine.frame(id).map(|f| (id, f)))
            .collect::<Result<_>>()?;
        autoreleasepool(|_| {
            let _transaction = NoAnimations::new();
            if self.root != engine.root() {
                if let Some(e) = self.entries.get(&self.root) {
                    e.view.removeFromSuperview();
                }
            }
            for (old, new) in super::row_recycling::plan(&self.applied, engine) {
                if let Some(view) = self.entries.remove(&old) {
                    if !view.hosted_type.is_empty() {
                        if let Some(child) = &view.extra {
                            crate::platform_view::rebind(
                                old,
                                new,
                                &view.hosted_type,
                                Retained::as_ptr(child).cast(),
                            );
                        }
                    }
                    self.entries.insert(new, view);
                    crate::events::rebind(old, new);
                }
            }
            crate::events::retain_bindings(|id| engine.node(id).is_some());
            self.entries.retain(|id, e| {
                if engine.node(*id).is_some() {
                    true
                } else {
                    e.release_platform_view(*id);
                    e.view.removeFromSuperview();
                    false
                }
            });
            for (id, n) in engine.nodes() {
                let e = self
                    .entries
                    .entry(id)
                    .or_insert_with(|| Entry::new(id, n.kind, self.mtm));
                if n.kind == Kind::PlatformView && e.hosted_type != n.platform_view.as_ref() {
                    e.release_platform_view(id);
                    e.hosted_type = n.platform_view.to_string();
                    if let Some(pointer) =
                        crate::platform_view::create(id, n.platform_view.as_ref())
                    {
                        let child =
                            unsafe { Retained::<NSView>::from_raw(pointer.as_ptr().cast()) }
                                .expect("platform view");
                        child.setAutoresizingMask(
                            objc2_app_kit::NSAutoresizingMaskOptions::ViewWidthSizable
                                | objc2_app_kit::NSAutoresizingMaskOptions::ViewHeightSizable,
                        );
                        e.view.addSubview(&child);
                        e.extra = Some(child);
                    }
                }
                if let Some(scroll) = e
                    .extra
                    .as_ref()
                    .and_then(|v| v.downcast_ref::<scroll::NativeScroll>())
                {
                    scroll.begin_update();
                }
                if self.applied.get(&id).is_none_or(|o| {
                    o.text != n.text
                        || o.style != n.style
                        || o.typography != n.typography
                        || o.resolved_flow != n.resolved_flow
                        || o.extended_layout != n.extended_layout
                        || o.padding() != n.padding()
                        || o.control != n.control
                        || o.image != n.image
                        || o.image_tint != n.image_tint
                }) {
                    // Native controls inherit their appearance from the surface
                    // they are drawn on, which may differ from the window theme.
                    // Transparent wrappers keep inheriting their parent's choice.
                    let [r, g, b, alpha] = n.style.background;
                    let appearance = (alpha == 255)
                        .then(|| {
                            let light = 299 * r as u32 + 587 * g as u32 + 114 * b as u32 >= 128_000;
                            objc2_app_kit::NSAppearance::appearanceNamed(unsafe {
                                if light {
                                    objc2_app_kit::NSAppearanceNameAqua
                                } else {
                                    objc2_app_kit::NSAppearanceNameDarkAqua
                                }
                            })
                        })
                        .flatten();
                    if e.view.appearance().as_deref() != appearance.as_deref() {
                        e.view.setAppearance(appearance.as_deref());
                    }
                    if let Some(layer) = e.view.layer() {
                        layer.setBackgroundColor(Some(&color(n.style.background).CGColor()));
                        layer.setCornerRadius(n.style.radius as f64);
                        layer.setMasksToBounds(true);
                    }
                    if let Some(v) = &e.label {
                        configure_label(v, n);
                    }
                    if let Some(v) = &e.button {
                        configure_button(v, n);
                    }
                    if let Some(v) = &e.extra {
                        match n.kind {
                            Kind::RadioButton => {
                                configure_radio(v.downcast_ref::<NSButton>().unwrap(), n)
                            }
                            Kind::TextField => {
                                configure_field(v.downcast_ref::<NSTextField>().unwrap(), n)
                            }
                            Kind::Switch => v
                                .downcast_ref::<NSSwitch>()
                                .unwrap()
                                .setState(if n.control.value != 0. { 1 } else { 0 }),
                            Kind::Slider => {
                                let slider = v.downcast_ref::<NSSlider>().unwrap();
                                slider.setMinValue(n.control.minimum as f64);
                                slider.setMaxValue(n.control.maximum as f64);
                                slider.setDoubleValue(n.control.value as f64);
                            }
                            Kind::Image | Kind::Icon => {
                                let image = v.downcast_ref::<NSImageView>().unwrap();
                                image.setImage(Some(&decoded_image(n).expect("validated image")));
                                image.setContentTintColor(
                                    n.image_tint
                                        .or_else(|| {
                                            (n.kind == Kind::Icon).then_some(n.style.foreground)
                                        })
                                        .map(color)
                                        .as_deref(),
                                );
                                image.setImageScaling(if n.kind == Kind::Image {
                                    NSImageScaling::ScaleAxesIndependently
                                } else {
                                    NSImageScaling::ScaleProportionallyUpOrDown
                                });
                            }
                            _ => (),
                        }
                    }
                }
            }
            let mut row_hosts = HashMap::new();
            for (_, list) in engine.nodes().filter(|(_, n)| n.virtual_list) {
                let document = list.children[0];
                let rows = &engine.node(document).unwrap().children;
                let views: Vec<_> = rows
                    .iter()
                    .map(|id| (*id, self.entries[id].view.clone()))
                    .collect();
                let entry = self.entries.get_mut(&document).unwrap();
                entry
                    .cells
                    .sync(
                        rows,
                        || Ok(FlippedView::make(self.mtm)),
                        |cell| {
                            for child in cell.subviews() {
                                child.removeFromSuperview();
                            }
                            cell.removeFromSuperview();
                        },
                    )
                    .expect("AppKit cell creation is infallible");
                for (id, view) in views {
                    let cell = entry.cells.get(id);
                    if unsafe { cell.superview() }.as_deref() != Some(&entry.view) {
                        entry.view.addSubview(cell);
                    }
                    if unsafe { view.superview() }.as_deref() != Some(cell) {
                        cell.addSubview(&view);
                    }
                    let f = engine.frame(id).expect("preflighted row frame");
                    cell.setFrame(CGRect::new(
                        CGPoint::new(f.x as f64, f.y as f64),
                        CGSize::new(f.width as f64, f.height as f64),
                    ));
                    row_hosts.insert(id, cell.clone());
                }
            }
            for (id, old) in &self.applied {
                if engine.node(*id).is_some_and(|n| n.children != old.children) {
                    for child in &old.children {
                        if let Some(e) = self
                            .entries
                            .get(child)
                            .filter(|_| !row_hosts.contains_key(child))
                        {
                            e.view.removeFromSuperview();
                        }
                    }
                }
            }
            for (id, n) in engine.nodes() {
                if self
                    .applied
                    .get(&id)
                    .is_none_or(|old| old.children != n.children)
                {
                    for child in &n.children {
                        if row_hosts.contains_key(child) {
                            continue;
                        }
                        self.entries[&id]
                            .document
                            .as_deref()
                            .unwrap_or(&self.entries[&id].view)
                            .addSubview(&self.entries[child].view);
                    }
                }
            }
            for (id, node) in engine.nodes() {
                if self.applied.get(&id).is_none_or(|old| {
                    old.paint_children != node.paint_children || old.children != node.children
                }) {
                    let mut order: HashMap<usize, usize> = node
                        .paint_children
                        .iter()
                        .enumerate()
                        .map(|(index, child)| {
                            (
                                Retained::as_ptr(
                                    row_hosts.get(child).unwrap_or(&self.entries[child].view),
                                ) as usize,
                                index + 1,
                            )
                        })
                        .collect();
                    let parent = self.entries[&id]
                        .document
                        .as_deref()
                        .unwrap_or(&self.entries[&id].view);
                    // AppKit invokes the comparator synchronously; the map and retained views live through this call.
                    unsafe {
                        parent.sortSubviewsUsingFunction_context(
                            compare_paint_order,
                            (&mut order as *mut HashMap<usize, usize>).cast(),
                        );
                    }
                }
            }
            if engine.root() != 0 && self.root != engine.root() {
                self.host.addSubview(&self.entries[&engine.root()].view);
            }
            self.frames.retain(|id, _| engine.node(*id).is_some());
            let mut moved = HashSet::new();
            let sidebar_children: HashSet<_> = engine
                .nodes()
                .filter(|(_, node)| node.kind == Kind::DesktopNavigation)
                .flat_map(|(_, node)| node.children.iter().copied())
                .collect();
            for (id, f) in frames {
                let e = &self.entries[&id];
                if let Some(sidebar) = &e.sidebar {
                    sidebar.sync(engine.node(id).unwrap(), f.width as f64, f.height as f64);
                }
                let p = engine.node(id).unwrap().padding().map(f64::from);
                let geometry_changed = self.frames.get(&id) != Some(&f);
                if geometry_changed {
                    moved.insert(id);
                }
                let padding_changed = self
                    .applied
                    .get(&id)
                    .is_none_or(|old| old.padding().map(f64::from) != p);
                if geometry_changed {
                    if let Some(layer) = e.view.layer() {
                        layer.setTransform(objc2_quartz_core::CATransform3D::new_scale(1., 1., 1.));
                    }
                    let in_sidebar = sidebar_children.contains(&id);
                    e.view.setFrame(CGRect::new(
                        CGPoint::new(
                            if in_sidebar || row_hosts.contains_key(&id) {
                                0.
                            } else {
                                f.x as f64
                            },
                            if row_hosts.contains_key(&id) {
                                0.
                            } else {
                                f.y as f64
                            },
                        ),
                        CGSize::new(f.width as f64, f.height as f64),
                    ));
                    self.frames.insert(id, f);
                }
                if geometry_changed
                    || padding_changed
                    || self.applied.get(&id).is_none_or(|old| {
                        old.image_fit != engine.node(id).unwrap().image_fit
                            || old.image != engine.node(id).unwrap().image
                    })
                {
                    let inner = CGRect::new(
                        CGPoint::new(p[0], p[1]),
                        CGSize::new(
                            (f.width as f64 - p[0] - p[2]).max(0.),
                            (f.height as f64 - p[1] - p[3]).max(0.),
                        ),
                    );
                    if let Some(v) = &e.label {
                        v.setFrame(inner);
                    }
                    if let Some(v) = &e.button {
                        v.setFrame(inner);
                    }
                    if let Some(v) = &e.extra {
                        v.setFrame(if engine.node(id).unwrap().kind == Kind::Image {
                            let image = v.downcast_ref::<NSImageView>().unwrap().image().unwrap();
                            let size = image.size();
                            let r = crate::image_fit::rect(
                                engine.node(id).unwrap().image_fit,
                                [size.width as f32, size.height as f32],
                                [inner.size.width as f32, inner.size.height as f32],
                            );
                            CGRect::new(
                                CGPoint::new(
                                    inner.origin.x + r[0] as f64,
                                    inner.origin.y + r[1] as f64,
                                ),
                                CGSize::new(r[2] as f64, r[3] as f64),
                            )
                        } else if e.document.is_some() {
                            CGRect::new(CGPoint::ZERO, CGSize::new(f.width as f64, f.height as f64))
                        } else {
                            inner
                        });
                    }
                }
                if let Some(doc) = e.document.as_ref().filter(|_| e.sidebar.is_none()) {
                    let extent = engine
                        .node(id)
                        .unwrap()
                        .children
                        .iter()
                        .filter_map(|child| engine.frame(*child).ok())
                        .fold(CGSize::new(f.width as f64, 0.), |size, c| {
                            CGSize::new(
                                size.width.max((c.x + c.width) as f64),
                                size.height.max((c.y + c.height) as f64),
                            )
                        });
                    let horizontal = engine.node(id).unwrap().scroll_axis == 1;
                    let scroll = e
                        .extra
                        .as_ref()
                        .unwrap()
                        .downcast_ref::<NSScrollView>()
                        .unwrap();
                    if self
                        .applied
                        .get(&id)
                        .is_none_or(|old| old.scroll_axis != engine.node(id).unwrap().scroll_axis)
                    {
                        scroll.setHasHorizontalScroller(horizontal);
                        scroll.setHasVerticalScroller(!horizontal);
                    }
                    // Classic (non-overlay) scrollers consume viewport space.
                    let viewport = scroll.contentView().bounds().size;
                    let extent = if horizontal {
                        CGSize::new(extent.width.max(viewport.width), viewport.height)
                    } else {
                        CGSize::new(viewport.width, extent.height.max(viewport.height))
                    };
                    let document_frame = CGRect::new(CGPoint::ZERO, extent);
                    if doc.frame() != document_frame {
                        doc.setFrame(document_frame);
                    }
                    if self
                        .applied
                        .get(&id)
                        .is_some_and(|old| old.scroll_axis != engine.node(id).unwrap().scroll_axis)
                    {
                        doc.scrollPoint(CGPoint::ZERO);
                    }
                    scroll
                        .downcast_ref::<scroll::NativeScroll>()
                        .unwrap()
                        .configure(engine.node(id).unwrap(), !self.applied.contains_key(&id));
                }
                let n = engine.node(id).unwrap();
                if n.kind == Kind::TextField
                    && n.control.focus_revision > 0
                    && self
                        .applied
                        .get(&id)
                        .is_none_or(|old| old.control.focus_revision != n.control.focus_revision)
                {
                    if let Some(window) = e.view.window() {
                        window.makeFirstResponder(if n.control.focus_requested {
                            e.extra.as_deref().map(|v| &**v)
                        } else {
                            None
                        });
                    }
                }
            }
            if let Some(window) = self.host.window() {
                // Window chrome (including the toolbar) lives outside the sidebar
                // view hierarchy and must follow the application's requested theme.
                let mode = engine.node(engine.root()).and_then(|root| match root.kind {
                    Kind::Navigation => Some(root.control.focus_revision),
                    Kind::DesktopNavigation => Some(root.desktop_navigation.theme),
                    _ => None,
                });
                if let Some(mode) = mode {
                    let appearance = match mode {
                        1 => objc2_app_kit::NSAppearance::appearanceNamed(unsafe {
                            objc2_app_kit::NSAppearanceNameAqua
                        }),
                        2 => objc2_app_kit::NSAppearance::appearanceNamed(unsafe {
                            objc2_app_kit::NSAppearanceNameDarkAqua
                        }),
                        _ => None,
                    };
                    if window.appearance().as_deref() != appearance.as_deref() {
                        window.setAppearance(appearance.as_deref());
                    }
                }
                let header = engine.nodes().find(|(id, node)| {
                    node.kind == Kind::NavigationHeader
                        && navigation::active_descendant(engine, *id)
                });
                match header {
                    Some((id, node)) if node.header.visible => {
                        if self.toolbar.is_none()
                            || self.applied.get(&id).is_none_or(|old| {
                                old.text != node.text || old.header != node.header
                            })
                        {
                            let bar = toolbar::NativeToolbar::make(id, node, self.mtm);
                            window.setTitle(&NSString::from_str(&node.text));
                            window.setToolbarStyle(objc2_app_kit::NSWindowToolbarStyle::Unified);
                            window.setToolbar(Some(&bar));
                            self.toolbar = Some(bar);
                        }
                    }
                    _ if self.toolbar.is_some() => {
                        window.setToolbar(None);
                        self.toolbar = None;
                    }
                    _ => (),
                }
            }
            self.root = engine.root();
            for (id, node) in engine.nodes() {
                if !super::apple_box::layer_needs_sync(
                    self.applied.get(&id).map(|old| &**old),
                    node,
                    moved.contains(&id),
                ) {
                    continue;
                }
                let entry = &self.entries[&id];
                if let Some(button) = &entry.button {
                    button.setEnabled(node.enabled);
                }
                if let Some(extra) = &entry.extra {
                    if let Some(control) = extra.downcast_ref::<objc2_app_kit::NSControl>() {
                        control.setEnabled(node.enabled);
                    }
                }
                // Navigation owns page visibility, including both pages while
                // animating. Reapplying hidden during a style update can cancel
                // AppKit's backing-layer animation on older macOS releases.
                if node.kind != Kind::NavigationPage {
                    self.entries[&id]
                        .view
                        .setHidden(node.behavior.visibility != 0);
                    // Keep AppKit's alpha in sync as well as the backing layer;
                    // newly mounted views initialize their layer from this value.
                    self.entries[&id]
                        .view
                        .setAlphaValue(node.visual.opacity as f64);
                }
            }
            // Complete AppKit view updates before applying layer transforms.
            // A parent's visibility/alpha update can reset its descendants' layers.
            for (id, node) in engine.nodes() {
                if !super::apple_box::layer_needs_sync(
                    self.applied.get(&id).map(|old| &**old),
                    node,
                    moved.contains(&id),
                ) {
                    continue;
                }
                if let Some(layer) = self.entries[&id].view.layer() {
                    if node.kind != Kind::NavigationPage {
                        let transform = apply_visual(&layer, node);
                        self.entries[&id]
                            .view
                            .downcast_ref::<FlippedView>()
                            .unwrap()
                            .set_visual_transform(transform);
                    }
                    apply_gradient(&layer, node);
                    super::apple_background::sync(
                        &layer,
                        node,
                        &mut self.entries.get_mut(&id).unwrap().background,
                        self.applied
                            .get(&id)
                            .is_none_or(|old| old.background_image != node.background_image),
                    );
                    super::apple_box::apply(&layer, node);
                }
            }
            self.applied = engine.presentation_nodes();
        });
        self.sync_navigation(engine);
        Ok(())
    }
}
impl Drop for Registry {
    fn drop(&mut self) {
        if self.toolbar.is_some() {
            if let Some(window) = self.host.window() {
                window.setToolbar(None);
            }
        }
        for e in self.entries.values() {
            e.view.removeFromSuperview();
        }
    }
}
struct NativeMeasurer {
    radio: Option<Retained<NSButton>>,
    field: Option<Retained<NSTextField>>,
    toggle: Option<Retained<NSSwitch>>,
    slider: Option<Retained<NSSlider>>,
    label: Retained<NSTextField>,
    button: Retained<NSButton>,
}
impl Measure for NativeMeasurer {
    fn validate_style(&self, node: &Node) -> Result<()> {
        if node.kind == Kind::TextField
            && node.text_input != crate::protocol::TextInputOptions::default()
        {
            return Err(Error::Tree(
                "TextInputOptions currently requires iOS or Android",
            ));
        }
        if node.kind == Kind::BottomNavigationBar {
            return Err(Error::Tree(
                "BottomNavigationBar is supported only on iOS and Android",
            ));
        }
        super::apple_fonts::register(node)
    }
    fn measure(
        &mut self,
        n: &Node,
        known: Size<Option<f32>>,
        available: Size<AvailableSpace>,
    ) -> Result<Size<f32>> {
        autoreleasepool(|_| {
            // The window owns the toolbar; its marker consumes no content space.
            if n.kind == Kind::PlatformView {
                return Ok(Size {
                    width: known.width.unwrap_or(0.),
                    height: known.height.unwrap_or(0.),
                });
            }
            if n.kind == Kind::NavigationHeader {
                return Ok(Size {
                    width: known.width.unwrap_or(0.),
                    height: 0.,
                });
            }
            if matches!(n.kind, Kind::Image | Kind::Icon) {
                let size = decoded_image(n)?.size();
                return Ok(Size {
                    width: known.width.unwrap_or(size.width as f32),
                    height: known.height.unwrap_or(size.height as f32),
                });
            }
            if n.kind == Kind::Switch {
                let view = self.toggle.get_or_insert_with(|| {
                    NSSwitch::initWithFrame(NSSwitch::alloc(self.label.mtm()), CGRect::ZERO)
                });
                let size = view.fittingSize();
                return Ok(Size {
                    width: known.width.unwrap_or(size.width as f32),
                    height: known.height.unwrap_or(size.height as f32),
                });
            }
            if n.kind == Kind::Slider {
                let view = self.slider.get_or_insert_with(|| {
                    NSSlider::initWithFrame(
                        NSSlider::alloc(self.label.mtm()),
                        CGRect::new(CGPoint::ZERO, CGSize::new(120., 0.)),
                    )
                });
                let size = view.fittingSize();
                return Ok(Size {
                    width: known.width.unwrap_or(size.width.max(120.) as f32),
                    height: known.height.unwrap_or(size.height as f32),
                });
            }
            let cell = match n.kind {
                Kind::RadioButton => {
                    let radio = self
                        .radio
                        .get_or_insert_with(|| NativeRadio::make(0, self.label.mtm()));
                    configure_radio(radio, n);
                    radio.cell()
                }
                Kind::TextField => {
                    let field = self.field.get_or_insert_with(|| {
                        NSTextField::initWithFrame(
                            NSTextField::alloc(self.label.mtm()),
                            CGRect::ZERO,
                        )
                    });
                    configure_field(field, n);
                    field.cell()
                }
                Kind::Text => {
                    configure_label(&self.label, n);
                    self.label.cell()
                }
                Kind::Button => {
                    configure_button(&self.button, n);
                    self.button.cell()
                }
                _ => return Err(Error::Measurement("container measure")),
            }
            .ok_or(Error::Measurement("missing AppKit cell"))?;
            let width = known.width.map(f64::from).unwrap_or(match available.width {
                AvailableSpace::Definite(v) => v.max(0.) as f64,
                AvailableSpace::MinContent => 0.,
                AvailableSpace::MaxContent => 1e9,
            });
            let size = cell.cellSizeForBounds(CGRect::new(CGPoint::ZERO, CGSize::new(width, 1e9)));
            Ok(Size {
                width: known.width.unwrap_or(size.width as f32),
                height: known.height.unwrap_or(size.height as f32),
            })
        })
    }
}
thread_local! {static REGISTRY:RefCell<Option<Registry>>=const {RefCell::new(None)};}
/// # Safety
/// Host is a live main-thread NSView, retained here for the session lifetime.
#[no_mangle]
pub unsafe extern "C" fn dotnative_macos_initialize(host: *mut NSView) -> i32 {
    let Some(mtm) = MainThreadMarker::new() else {
        return -1;
    };
    let Some(host) = (unsafe { Retained::retain(host) }) else {
        return -1;
    };
    if crate::initialize(Box::new(NativeMeasurer {
        radio: None,
        field: None,
        toggle: None,
        slider: None,
        label: label(mtm),
        button: NSButton::initWithFrame(NSButton::alloc(mtm), CGRect::ZERO),
    }))
    .is_err()
    {
        return -1;
    }
    REGISTRY.with(|r| {
        *r.borrow_mut() = Some(Registry {
            toolbar: None,
            navigation: HashMap::new(),
            host,
            entries: HashMap::new(),
            applied: im::HashMap::new(),
            frames: HashMap::new(),
            root: 0,
            mtm,
        })
    });
    0
}
pub(crate) fn flush(bytes: &[u8]) -> Result<()> {
    REGISTRY.with(|r| {
        let mut r = r
            .try_borrow_mut()
            .map_err(|_| Error::Tree("reentrant AppKit sync"))?;
        let r = r
            .as_mut()
            .ok_or(Error::Tree("initialize AppKit before flush"))?;
        crate::ENGINE.with(|e| {
            let mut e = e
                .try_borrow_mut()
                .map_err(|_| Error::Tree("reentrant engine"))?;
            let e = e.as_mut().ok_or(Error::Tree("missing engine"))?;
            e.flush(bytes)?;
            e.report_layouts()?;
            r.sync(e)
        })
    })
}

#[cfg(test)]
mod font_tests {
    fn font_fixture(name: &str, embedded: &[u8]) -> Vec<u8> {
        if let Some(directory) = std::env::var_os("DOTNATIVE_FONT_COLLECTION_FIXTURES") {
            std::fs::read(std::path::Path::new(&directory).join(name)).unwrap()
        } else {
            embedded.to_vec()
        }
    }
    #[test]
    fn asset_family_and_variation_change_native_glyph_metrics() {
        let mut engine = crate::tests::engine();
        engine.flush(&crate::tests::initial()).unwrap();
        let mut node = engine.node(2).unwrap().clone();
        node.typography.family = std::sync::Arc::from("DotNativeTest");
        node.font_asset = Some(
            crate::font_assets::FontAsset::new(
                "DotNativeTest",
                &font_fixture(
                    "DotNativeTest.ttf",
                    include_bytes!("../../tests/fixtures/DotNativeTest.ttf"),
                ),
            )
            .unwrap(),
        );
        super::super::apple_fonts::register(&node).unwrap();
        let regular_asset = node.font_asset.clone();
        let font = super::styled_font(&node);
        assert_eq!(font.familyName().unwrap().to_string(), "DotNativeTest");
        let narrow = font.advancementForGlyph(2).width;
        node.font_axes = std::sync::Arc::from([(*b"wdth", 200.)]);
        let wide = super::styled_font(&node).advancementForGlyph(2).width;
        assert!(
            wide > narrow * 1.5,
            "native variation metrics: {narrow} -> {wide}"
        );
        node.font_axes = std::sync::Arc::from([]);
        assert_eq!(
            super::styled_font(&node).advancementForGlyph(2).width,
            narrow
        );
        node.font_asset = Some(
            crate::font_assets::FontAsset::new(
                "DotNativeTest",
                &font_fixture(
                    "DotNativeTest-Bold.ttf",
                    include_bytes!("../../tests/fixtures/DotNativeTest-Bold.ttf"),
                ),
            )
            .unwrap(),
        );
        super::super::apple_fonts::register(&node).unwrap();
        node.typography.weight = 700;
        let bold = super::styled_font(&node);
        assert_eq!(bold.familyName().unwrap().to_string(), "DotNativeTest");
        assert!(bold.advancementForGlyph(2).width > narrow * 1.4);
        node.typography.weight = 400;
        // An explicitly selected Bold asset stays Bold even with the default
        // weight. Restore the regular asset to test switching back to that face.
        assert_eq!(
            super::styled_font(&node).fontName().to_string(),
            bold.fontName().to_string()
        );
        node.font_asset = regular_asset;
        assert_eq!(
            super::styled_font(&node).advancementForGlyph(2).width,
            narrow
        );
        if let Some(directory) = std::env::var_os("DOTNATIVE_FONT_COLLECTION_FIXTURES") {
            let cff =
                std::fs::read(std::path::Path::new(&directory).join("DotNativeCff.otf")).unwrap();
            node.typography.family = "DotNativeCff".into();
            node.font_asset =
                Some(crate::font_assets::FontAsset::new("DotNativeCff", &cff).unwrap());
            super::super::apple_fonts::register(&node).unwrap();
            let font = super::styled_font(&node);
            assert_eq!(font.familyName().unwrap().to_string(), "DotNativeCff");
            assert!(
                (font.advancementForGlyph(1).width - node.style.font_size as f64 * 0.6).abs()
                    < 0.01
            );
        }
    }
}
