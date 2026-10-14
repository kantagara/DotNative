//! UIKit presentation for committed engine state. All objects are main-thread-only.
mod bottom_navigation;
mod collection;
mod navigation;
mod scroll;
mod text_input;
use crate::{AvailableSpace, Engine, Error, Kind, Measure, Node, Result, Size, Style};
use objc2::{
    define_class, msg_send,
    rc::{autoreleasepool, Retained},
    sel, DefinedClass, MainThreadMarker, MainThreadOnly,
};
use objc2_core_foundation::{CGFloat, CGPoint, CGRect, CGSize};
use objc2_foundation::{NSInteger, NSString};
use objc2_quartz_core::CATransaction;
use objc2_ui_kit::{
    NSLineBreakMode, UIButton, UIButtonType, UIColor, UIControlEvents, UIControlState,
    UIEdgeInsets, UIFont, UILabel, UIView, UIViewAutoresizing,
};
use objc2_ui_kit::{
    UIImage, UIImageView, UIScrollView, UISlider, UISwitch, UITextField, UIViewContentMode,
};
use std::{
    cell::Cell,
    collections::{HashMap, HashSet},
};
struct InputIvars {
    id: u32,
    padding: Cell<[CGFloat; 4]>,
}
define_class!(
    #[unsafe(super(UITextField))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeTextField"]
    #[ivars=InputIvars]
    struct NativeTextField;
    impl NativeTextField {
        #[unsafe(method(dotnativeHover:))]
        fn hover(&self, recognizer: &objc2_ui_kit::UIHoverGestureRecognizer) { let state=recognizer.state(); if state==objc2_ui_kit::UIGestureRecognizerState::Began || state==objc2_ui_kit::UIGestureRecognizerState::Ended || state==objc2_ui_kit::UIGestureRecognizerState::Cancelled {crate::events::emit(self.ivars().id,8,if state==objc2_ui_kit::UIGestureRecognizerState::Began {1.}else{0.},String::new());} }
        #[unsafe(method(dotnativeText:))]
        fn changed(&self,_sender:&UITextField){crate::events::emit(self.ivars().id,2,0.,self.text().map(|s|s.to_string()).unwrap_or_default());}
        #[unsafe(method(dotnativeFocus:))]
        fn focused(&self,_sender:&UITextField){crate::events::emit(self.ivars().id,4,1.,String::new());}
        #[unsafe(method(dotnativeBlur:))]
        fn blurred(&self,_sender:&UITextField){crate::events::emit(self.ivars().id,4,0.,String::new());}
        #[unsafe(method(dotnativeSubmit:))]
        fn submitted(&self,_sender:&UITextField){crate::events::emit(self.ivars().id,9,0.,self.text().map(|s|s.to_string()).unwrap_or_default());}
        #[unsafe(method(textRectForBounds:))]
        fn text_rect(&self,bounds:CGRect)->CGRect {inset(bounds,self.ivars().padding.get())}
        #[unsafe(method(editingRectForBounds:))]
        fn editing_rect(&self,bounds:CGRect)->CGRect {inset(bounds,self.ivars().padding.get())}
    }
);
impl NativeTextField {
    fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(InputIvars {
            id,
            padding: Cell::new([0.; 4]),
        });
        let field: Retained<Self> =
            unsafe { msg_send![super(allocated),initWithFrame:CGRect::ZERO] };
        unsafe {
            field.addTarget_action_forControlEvents(
                Some(&field),
                sel!(dotnativeText:),
                UIControlEvents::EditingChanged,
            );
            field.addTarget_action_forControlEvents(
                Some(&field),
                sel!(dotnativeSubmit:),
                UIControlEvents::EditingDidEndOnExit,
            );
            field.addTarget_action_forControlEvents(
                Some(&field),
                sel!(dotnativeFocus:),
                UIControlEvents::EditingDidBegin,
            );
            field.addTarget_action_forControlEvents(
                Some(&field),
                sel!(dotnativeBlur:),
                UIControlEvents::EditingDidEnd,
            );
        }
        attach_hover(&field);
        field
    }
}
define_class!(
    #[unsafe(super(UISwitch))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeSwitch"]
    #[ivars=u32]
    struct NativeSwitch;
    impl NativeSwitch {
        #[unsafe(method(dotnativeHover:))]
        fn hover(&self, recognizer: &objc2_ui_kit::UIHoverGestureRecognizer) { let state=recognizer.state(); if state==objc2_ui_kit::UIGestureRecognizerState::Began || state==objc2_ui_kit::UIGestureRecognizerState::Ended || state==objc2_ui_kit::UIGestureRecognizerState::Cancelled {crate::events::emit(*self.ivars(),8,if state==objc2_ui_kit::UIGestureRecognizerState::Began {1.}else{0.},String::new());} }
        #[unsafe(method(becomeFirstResponder))]
        fn become_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), becomeFirstResponder] };
            if accepted { crate::events::emit(*self.ivars(), 4, 1., String::new()); }
            accepted
        }
        #[unsafe(method(resignFirstResponder))]
        fn resign_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), resignFirstResponder] };
            if accepted { crate::events::emit(*self.ivars(), 4, 0., String::new()); }
            accepted
        }
        #[unsafe(method(dotnativeValue:))]
        fn changed(&self,_sender:&UISwitch){crate::events::emit(*self.ivars(),3,if self.isOn(){1.}else{0.},String::new());}
    }
);
impl NativeSwitch {
    fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(id);
        let view: Retained<Self> =
            unsafe { msg_send![super(allocated),initWithFrame:CGRect::ZERO] };
        unsafe {
            view.addTarget_action_forControlEvents(
                Some(&view),
                sel!(dotnativeValue:),
                UIControlEvents::ValueChanged,
            );
        }
        attach_hover(&view);
        view
    }
}
define_class!(
    #[unsafe(super(UISlider))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeSlider"]
    #[ivars=u32]
    struct NativeSlider;
    impl NativeSlider {
        #[unsafe(method(dotnativeHover:))]
        fn hover(&self, recognizer: &objc2_ui_kit::UIHoverGestureRecognizer) { let state=recognizer.state(); if state==objc2_ui_kit::UIGestureRecognizerState::Began || state==objc2_ui_kit::UIGestureRecognizerState::Ended || state==objc2_ui_kit::UIGestureRecognizerState::Cancelled {crate::events::emit(*self.ivars(),8,if state==objc2_ui_kit::UIGestureRecognizerState::Began {1.}else{0.},String::new());} }
        #[unsafe(method(becomeFirstResponder))]
        fn become_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), becomeFirstResponder] };
            if accepted { crate::events::emit(*self.ivars(), 4, 1., String::new()); }
            accepted
        }
        #[unsafe(method(resignFirstResponder))]
        fn resign_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), resignFirstResponder] };
            if accepted { crate::events::emit(*self.ivars(), 4, 0., String::new()); }
            accepted
        }
        #[unsafe(method(dotnativeValue:))]
        fn changed(&self,_sender:&UISlider){crate::events::emit(*self.ivars(),3,self.value(),String::new());}
    }
);
impl NativeSlider {
    fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(id);
        let view: Retained<Self> =
            unsafe { msg_send![super(allocated),initWithFrame:CGRect::ZERO] };
        unsafe {
            view.addTarget_action_forControlEvents(
                Some(&view),
                sel!(dotnativeValue:),
                UIControlEvents::ValueChanged,
            );
        }
        attach_hover(&view);
        view
    }
}
pub(super) fn decoded_image(node: &Node) -> Result<Retained<UIImage>> {
    let image = if node.kind == Kind::Icon {
        let name = match &*node.text {
            "star" => "star.fill",
            "check" => "checkmark.circle.fill",
            "info" => "info.circle",
            _ => return Err(Error::Measurement("unknown icon")),
        };
        UIImage::systemImageNamed(&NSString::from_str(name))
    } else {
        let data = objc2_foundation::NSData::with_bytes(&node.image);
        UIImage::imageWithData(&data)
    };
    let image = image.ok_or(Error::Measurement("UIKit could not decode image"))?;
    // CALayer consumes raw CGImage pixels, unlike UIImageView which honors EXIF orientation.
    let image = if !node.background_image.is_empty()
        && unsafe { image.imageOrientation() != objc2_ui_kit::UIImageOrientation::Up }
    {
        use objc2::AnyThread;
        use objc2_ui_kit::{
            UIGraphicsImageRenderer, UIGraphicsImageRendererContext, UIGraphicsImageRendererFormat,
        };
        let size = unsafe { image.size() };
        let format = UIGraphicsImageRendererFormat::preferredFormat();
        format.setScale(unsafe { image.scale() });
        format.setOpaque(false);
        let renderer = UIGraphicsImageRenderer::initWithSize_format(
            UIGraphicsImageRenderer::alloc(),
            size,
            &format,
        );
        let draw = block2::RcBlock::new(
            move |_: std::ptr::NonNull<UIGraphicsImageRendererContext>| {
                image.drawInRect(CGRect::new(CGPoint::ZERO, size))
            },
        );
        unsafe { renderer.imageWithActions(block2::RcBlock::as_ptr(&draw)) }
    } else {
        image
    };
    if !node.background_image.is_empty() && unsafe { image.CGImage().is_none() } {
        return Err(Error::Measurement(
            "UIKit could not rasterize background image",
        ));
    }
    Ok(if node.kind == Kind::Image {
        image.imageWithRenderingMode(if node.image_tint.is_some() {
            objc2_ui_kit::UIImageRenderingMode::AlwaysTemplate
        } else {
            objc2_ui_kit::UIImageRenderingMode::AlwaysOriginal
        })
    } else {
        image
    })
}
fn configure_field(field: &UITextField, node: &Node) {
    text_input::traits(field, node);
    // Do not disturb the field editor/IME when an edit echoes back through State.
    let current = field.text().map(|s| s.to_string()).unwrap_or_default();
    let marked: Option<Retained<objc2::runtime::AnyObject>> =
        unsafe { msg_send![field, markedTextRange] };
    if current != &*node.text && marked.is_none() {
        field.setText(Some(&NSString::from_str(&node.text)));
    }
    field.setAttributedPlaceholder(None);
    field.setPlaceholder(Some(&NSString::from_str(&node.control.placeholder)));
    if let Some(hint_color) = node.text_input.placeholder_color {
        use objc2::AnyThread;
        use objc2_foundation::{NSMutableAttributedString, NSRange};
        use objc2_ui_kit::{NSFontAttributeName, NSForegroundColorAttributeName};
        let text = NSString::from_str(&node.control.placeholder);
        let hint =
            NSMutableAttributedString::initWithString(NSMutableAttributedString::alloc(), &text);
        let range = NSRange::new(0, text.length());
        unsafe {
            hint.addAttribute_value_range(
                NSForegroundColorAttributeName,
                &color(hint_color),
                range,
            );
            hint.addAttribute_value_range(NSFontAttributeName, &styled_font(node), range);
        }
        field.setAttributedPlaceholder(Some(&hint));
    }
    field.setFont(Some(&styled_font(node)));
    field.setTextColor(Some(&color(node.style.foreground)));
    field.setTextAlignment(text_alignment(node));
}

fn attach_hover(view: &UIView) {
    unsafe {
        let hover = objc2_ui_kit::UIHoverGestureRecognizer::initWithTarget_action(
            objc2_ui_kit::UIHoverGestureRecognizer::alloc(view.mtm()),
            Some(view),
            Some(sel!(dotnativeHover:)),
        );
        hover.setCancelsTouchesInView(false);
        view.addGestureRecognizer(&hover);
    }
}
struct ButtonIvars {
    id: u32,
}

define_class!(
    // UIKit keeps an unretained target. Using the button itself avoids a separate
    // target lifetime and cannot create a retain cycle.
    #[unsafe(super(UIButton))]
    #[thread_kind = MainThreadOnly]
    #[name = "DotNativeButton"]
    #[ivars = ButtonIvars]
    struct NativeButton;
    impl NativeButton {
        #[unsafe(method(dotnativeClick:))]
        fn clicked(&self, _sender: &UIButton) { crate::events::enqueue(self.ivars().id); }
        #[unsafe(method(becomeFirstResponder))]
        fn become_first_responder(&self) -> bool { let ok:bool=unsafe{msg_send![super(self),becomeFirstResponder]}; if ok {crate::events::emit(self.ivars().id,4,1.,String::new());} ok }
        #[unsafe(method(resignFirstResponder))]
        fn resign_first_responder(&self) -> bool { let ok:bool=unsafe{msg_send![super(self),resignFirstResponder]}; if ok {crate::events::emit(self.ivars().id,4,0.,String::new());} ok }
        #[unsafe(method(dotnativeHover:))]
        fn hover(&self, recognizer: &objc2_ui_kit::UIHoverGestureRecognizer) { let state=recognizer.state(); if state==objc2_ui_kit::UIGestureRecognizerState::Began || state==objc2_ui_kit::UIGestureRecognizerState::Ended || state==objc2_ui_kit::UIGestureRecognizerState::Cancelled {crate::events::emit(self.ivars().id,8,if state==objc2_ui_kit::UIGestureRecognizerState::Began {1.}else{0.},String::new());} }
        #[unsafe(method(setHighlighted:))]
        fn set_highlighted(&self, highlighted: bool) {
            crate::events::emit(self.ivars().id,7,if highlighted {1.} else {0.},String::new());
            // UIKit owns touch tracking, including drag-out and cancellation.
            unsafe { let _: () = msg_send![super(self), setHighlighted: highlighted]; }

        }
    }
);
impl NativeButton {
    fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(ButtonIvars { id });
        // SAFETY: UIButton initializer and exact target/action signature. The
        // receiver remains alive for as long as UIKit can deliver its action.
        let button: Retained<Self> =
            unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] };
        unsafe {
            button.addTarget_action_forControlEvents(
                Some(&button),
                sel!(dotnativeClick:),
                UIControlEvents::TouchUpInside,
            );
        }
        attach_hover(&button);
        button
    }
}

#[derive(Default)]
struct LabelIvars {
    padding: Cell<[CGFloat; 4]>,
}

define_class!(
    // SAFETY: UILabel permits subclassing. These signatures match UIKit exactly.
    // No Send/Sync implementations: allocation, access and destruction stay on main.
    #[unsafe(super(UILabel))]
    #[thread_kind = MainThreadOnly]
    #[name = "DotNativePaddedLabel"]
    #[ivars = LabelIvars]
    struct PaddedLabel;
    impl PaddedLabel {
        #[unsafe(method(textRectForBounds:limitedToNumberOfLines:))]
        fn text_rect(&self, bounds: CGRect, lines: NSInteger) -> CGRect {
            let p = self.ivars().padding.get();
            let inner = inset(bounds, p);
            // SAFETY: exact UILabel method signature, valid receiver and CGRect.
            let rect: CGRect = unsafe { msg_send![super(self), textRectForBounds: inner, limitedToNumberOfLines: lines] };
            CGRect::new(CGPoint::new(rect.origin.x-p[0], rect.origin.y-p[1]), CGSize::new(rect.size.width+p[0]+p[2],rect.size.height+p[1]+p[3]))
        }
        #[unsafe(method(drawTextInRect:))]
        fn draw_text(&self, rect: CGRect) {
            let rect = inset(rect, self.ivars().padding.get());
            // SAFETY: exact UILabel drawTextInRect: signature; UIKit calls on main.
            unsafe { let _: () = msg_send![super(self), drawTextInRect: rect]; }
        }
    }
);
impl PaddedLabel {
    fn new(mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(LabelIvars::default());
        // SAFETY: UILabel's designated initializer; initialized Rust ivars live
        // with the retained subclass and are dropped by objc2 on deallocation.
        unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] }
    }
}
fn inset(rect: CGRect, p: [CGFloat; 4]) -> CGRect {
    CGRect::new(
        CGPoint::new(rect.origin.x + p[0], rect.origin.y + p[1]),
        CGSize::new(
            (rect.size.width - p[0] - p[2]).max(0.),
            (rect.size.height - p[1] - p[3]).max(0.),
        ),
    )
}
fn attributed_text(node: &Node) -> Retained<objc2_foundation::NSMutableAttributedString> {
    use objc2::AnyThread;
    use objc2_foundation::{NSMutableAttributedString, NSNumber, NSRange};
    use objc2_ui_kit::{
        NSFontAttributeName, NSForegroundColorAttributeName, NSKernAttributeName,
        NSMutableParagraphStyle, NSParagraphStyleAttributeName, NSStrikethroughStyleAttributeName,
        NSUnderlineStyleAttributeName,
    };
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

fn styled_font(node: &Node) -> Retained<UIFont> {
    let weight = [-0.8, -0.6, -0.4, 0., 0.23, 0.3, 0.4, 0.56, 0.62]
        [(node.typography.weight / 100 - 1) as usize];
    let size = node.style.font_size as CGFloat;
    let base = UIFont::systemFontOfSize_weight(size, weight);
    // The protocol family is the Windows name ("Rounded Mplus 1c Bold"). Core Text
    // publishes that file under the Mac family and its PostScript name, so a family
    // descriptor misses the face and UIKit substitutes the system font.
    let mut descriptor = if let Some(face) = node
        .font_asset
        .as_ref()
        .and_then(|asset| UIFont::fontWithName_size(&NSString::from_str(&asset.face), size))
    {
        unsafe { face.fontDescriptor() }
    } else {
        let mut descriptor = unsafe { base.fontDescriptor() };
        if !node.typography.family.is_empty() {
            // A system descriptor retains a concrete font name; changing only its
            // family can still resolve to the system font. Start with family + traits.
            let family = NSString::from_str(&node.typography.family);
            descriptor = unsafe {
                let weight = objc2_foundation::NSNumber::new_f64(weight);
                let traits = objc2_foundation::NSDictionary::from_slices(
                    &[objc2_ui_kit::UIFontWeightTrait],
                    &[&*weight as &objc2::runtime::AnyObject],
                );
                let keys = [
                    objc2_ui_kit::UIFontDescriptorFamilyAttribute,
                    objc2_ui_kit::UIFontDescriptorTraitsAttribute,
                ];
                let values: [&objc2::runtime::AnyObject; 2] = [&*family, &*traits];
                objc2_ui_kit::UIFontDescriptor::fontDescriptorWithFontAttributes(
                    &objc2_foundation::NSDictionary::from_slices(&keys, &values),
                )
            };
        }
        descriptor
    };
    if node.typography.italic {
        let traits = unsafe { descriptor.symbolicTraits() }
            | objc2_ui_kit::UIFontDescriptorSymbolicTraits::TraitItalic;
        if let Some(italic) = descriptor.fontDescriptorWithSymbolicTraits(traits) {
            descriptor = italic;
        }
    }
    if !node.font_axes.is_empty() {
        descriptor = unsafe {
            descriptor
                .fontDescriptorByAddingAttributes(&super::apple_fonts::variation_attributes(node))
        };
    }
    UIFont::fontWithDescriptor_size(&descriptor, size)
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
fn text_alignment(node: &Node) -> objc2_ui_kit::NSTextAlignment {
    use objc2_ui_kit::NSTextAlignment;
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
    let colors: Vec<_> = g
        .stops
        .iter()
        .map(|(_, c)| unsafe { color(*c).CGColor() })
        .collect();
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

fn apply_visual(layer: &objc2_quartz_core::CALayer, node: &Node) {
    let v = node.visual;
    layer.setOpacity(v.opacity);
    layer.setBorderWidth(v.border_width as _);
    // UIKit objects are accessed only on the main thread; CALayer retains the colors.
    unsafe {
        layer.setBorderColor(Some(&color(v.border_color).CGColor()));
    }
    unsafe {
        layer.setShadowColor(Some(&color(v.shadow_color).CGColor()));
    }
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
    layer.setTransform(transform.concat(projection));
}

fn color(rgba: [u8; 4]) -> Retained<UIColor> {
    UIColor::colorWithRed_green_blue_alpha(
        rgba[0] as CGFloat / 255.,
        rgba[1] as CGFloat / 255.,
        rgba[2] as CGFloat / 255.,
        rgba[3] as CGFloat / 255.,
    )
}

/// Applies the validated Style decoded from SET_STYLE's 24-byte payload (4 bytes
/// are the node ID). Never reinterpret packed wire bytes as a Rust/ObjC struct.
/// Padding changes content placement only; Taffy already included it in the frame.
fn apply_style(view: &UIView, style: Style) {
    let background = style.background;
    view.setBackgroundColor(Some(&color(background)));
    let layer = view.layer();
    layer.setCornerRadius(style.radius as CGFloat);
    layer.setMasksToBounds(true);
    view.setOpaque(false); // arbitrary alpha colors, including transparent resets
}
fn configure_label(label: &UILabel, node: &Node) {
    label.setText(Some(&NSString::from_str(&node.text)));
    let font = styled_font(node);
    let foreground = color(node.style.foreground);
    // SAFETY: generated bindings flag potentially invalid nil. Both are non-nil,
    // valid retained UIKit objects; UILabel retains them as property values.
    unsafe {
        label.setFont(Some(&font));
        label.setTextColor(Some(&foreground));
    }
    label.setNumberOfLines(max_lines(node));
    label.setTextAlignment(text_alignment(node));
    label.setLineBreakMode(line_break(node));
    label.setAttributedText(Some(&attributed_text(node)));
}
#[allow(deprecated)] // Legacy custom button, deliberately no UIButtonConfiguration.
fn configure_button(button: &UIButton, node: &Node, padding: [CGFloat; 4]) {
    let title = attributed_text(node);
    button.setAttributedTitle_forState(Some(&title), UIControlState::Normal);
    button.setTitleColor_forState(Some(&color(node.style.foreground)), UIControlState::Normal);
    if let Some(label) = button.titleLabel() {
        // SAFETY: non-nil UIFont with lifetime retained by the UILabel property.
        unsafe {
            label.setFont(Some(&styled_font(node)));
        }
        label.setNumberOfLines(max_lines(node));
        label.setTextAlignment(text_alignment(node));
        label.setLineBreakMode(line_break(node));
        label.setAttributedText(Some(&title));
    }
    // Only inset visible content. Measurement prototypes always use zero here,
    // so protocol padding is included exactly once in Taffy's measured box.
    button.setContentEdgeInsets(UIEdgeInsets {
        top: padding[1],
        left: padding[0],
        bottom: padding[3],
        right: padding[2],
    });
}
define_class!(
    #[unsafe(super(UIButton))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeRadioButton"]
    #[ivars=u32]
    struct NativeRadio;
    impl NativeRadio {
        #[unsafe(method(dotnativeHover:))]
        fn hover(&self, recognizer: &objc2_ui_kit::UIHoverGestureRecognizer) { let state=recognizer.state(); if state==objc2_ui_kit::UIGestureRecognizerState::Began || state==objc2_ui_kit::UIGestureRecognizerState::Ended || state==objc2_ui_kit::UIGestureRecognizerState::Cancelled {crate::events::emit(*self.ivars(),8,if state==objc2_ui_kit::UIGestureRecognizerState::Began {1.}else{0.},String::new());} }
        #[unsafe(method(becomeFirstResponder))]
        fn become_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), becomeFirstResponder] };
            if accepted { crate::events::emit(*self.ivars(), 4, 1., String::new()); }
            accepted
        }
        #[unsafe(method(resignFirstResponder))]
        fn resign_first_responder(&self) -> bool {
            let accepted: bool = unsafe { msg_send![super(self), resignFirstResponder] };
            if accepted { crate::events::emit(*self.ivars(), 4, 0., String::new()); }
            accepted
        }
        #[unsafe(method(dotnativeSelect:))]
        fn select(&self, _sender:&UIButton) {
            if !self.isSelected() { crate::events::emit(*self.ivars(), 3, 1., String::new()); }
        }
        #[unsafe(method(setHighlighted:))]
        fn highlighted(&self, value: bool) {
            let changed = self.isHighlighted() != value;
            unsafe { let _: () = msg_send![super(self), setHighlighted: value]; }
            if changed { crate::events::emit(*self.ivars(), 7, if value {1.} else {0.}, String::new()); }
        }
    }
);
impl NativeRadio {
    fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(id);
        let control: Retained<Self> =
            unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] };
        unsafe {
            control.addTarget_action_forControlEvents(
                Some(&control),
                sel!(dotnativeSelect:),
                UIControlEvents::TouchUpInside,
            );
        }
        attach_hover(&control);
        control
    }
}
#[allow(deprecated)]
fn configure_radio(control: &UIButton, node: &Node, padding: [CGFloat; 4]) {
    configure_button(control, node, padding);
    // The indicator is an SF Symbol image, never a character in the label.
    control.setImage_forState(
        UIImage::systemImageNamed(&NSString::from_str("circle")).as_deref(),
        UIControlState::Normal,
    );
    control.setImage_forState(
        UIImage::systemImageNamed(&NSString::from_str("circle.inset.filled")).as_deref(),
        UIControlState::Selected,
    );
    control.setSelected(node.control.value == 1.);
    control.setTitleEdgeInsets(UIEdgeInsets {
        top: 0.,
        left: 8.,
        bottom: 0.,
        right: -8.,
    });
    control.setContentEdgeInsets(UIEdgeInsets {
        top: padding[1],
        left: padding[0],
        bottom: padding[3],
        right: padding[2] + 8.,
    });
    // UIButton contributes the Button role; Selected is reflected by UIKit's
    // accessibility implementation through the real UIControl selected state.
}
fn create_view(id: u32, node: &Node, mtm: MainThreadMarker) -> Retained<UIView> {
    let kind = node.kind;
    let view = match kind {
        Kind::TextField if node.text_input.multiline() => {
            Retained::into_super(Retained::into_super(Retained::into_super(
                text_input::NativeTextView::new(id, mtm),
            )))
        }
        Kind::ScrollView if node.virtual_list => collection::NativeCollection::new(id, mtm)
            .into_super()
            .into_super()
            .into_super(),
        Kind::ScrollView => {
            Retained::into_super(Retained::into_super(scroll::NativeScroll::new(id, mtm)))
        }
        Kind::TextField => Retained::into_super(Retained::into_super(Retained::into_super(
            NativeTextField::new(id, mtm),
        ))),
        Kind::RadioButton => Retained::into_super(Retained::into_super(Retained::into_super(
            NativeRadio::new(id, mtm),
        ))),
        Kind::Switch => Retained::into_super(Retained::into_super(Retained::into_super(
            NativeSwitch::new(id, mtm),
        ))),
        Kind::Slider => Retained::into_super(Retained::into_super(Retained::into_super(
            NativeSlider::new(id, mtm),
        ))),
        Kind::Image | Kind::Icon => Retained::into_super(UIImageView::initWithFrame(
            UIImageView::alloc(mtm),
            CGRect::ZERO,
        )),
        Kind::BottomNavigationBar => Retained::into_super(Retained::into_super(
            bottom_navigation::NativeTabBar::new(id, mtm),
        )),
        Kind::Navigation => Retained::into_super(navigation::NavigationView::new(id, mtm)),
        Kind::PlatformView => {
            let slot = UIView::initWithFrame(UIView::alloc(mtm), CGRect::ZERO);
            slot.setClipsToBounds(true);
            slot
        }
        Kind::VStack | Kind::DesktopNavigation | Kind::NavigationPage | Kind::NavigationHeader => {
            UIView::initWithFrame(UIView::alloc(mtm), CGRect::ZERO)
        }
        Kind::Text => Retained::into_super(Retained::into_super(PaddedLabel::new(mtm))),
        Kind::Button => Retained::into_super(Retained::into_super(Retained::into_super(
            NativeButton::new(id, mtm),
        ))),
    };
    // Taffy owns managed frames. UIScrollView must still resize its UIKit-owned
    // children (including large-title chrome on iOS 26). Managed children have
    // no autoresizing mask, so they remain under Taffy control.
    view.setAutoresizingMask(UIViewAutoresizing::None);
    view.setAutoresizesSubviews(kind == Kind::ScrollView || kind == Kind::PlatformView);
    view
}

/// Owns one retain per node. UIKit also retains attached subviews; deletion must
/// detach AND release the registry retain. The supplied host is runner-owned and
/// must not be one of this registry's managed views or have layout constraints on them.
pub struct NativeRegistry {
    views: HashMap<u32, Retained<UIView>>,
    hosted: HashMap<u32, (String, Retained<UIView>)>,
    backgrounds: HashMap<u32, Option<super::apple_background::Background>>,
    applied: im::HashMap<u32, std::sync::Arc<Node>>,
    frames: HashMap<u32, crate::engine::Frame>,
    host: Retained<UIView>,
    mounted_root: u32,
    mtm: MainThreadMarker,
}
impl NativeRegistry {
    pub fn new(host: Retained<UIView>, mtm: MainThreadMarker) -> Self {
        Self {
            views: HashMap::new(),
            hosted: HashMap::new(),
            backgrounds: HashMap::new(),
            applied: im::HashMap::new(),
            frames: HashMap::new(),
            host,
            mounted_root: 0,
            mtm,
        }
    }
    pub fn view(&self, id: u32) -> Option<&UIView> {
        self.views.get(&id).map(|v| &**v)
    }

    fn mount_platform_view(&mut self, id: u32, node: &Node) {
        if self
            .hosted
            .get(&id)
            .is_some_and(|(name, _)| name == node.platform_view.as_ref())
        {
            let bounds = self.views[&id].bounds();
            if let Some((_, child)) = self.hosted.get(&id) {
                child.setFrame(bounds);
            }
            return;
        }
        if let Some((name, child)) = self.hosted.remove(&id) {
            crate::platform_view::detach(id, &name, Retained::as_ptr(&child).cast());
            child.removeFromSuperview();
        }
        let Some(pointer) = crate::platform_view::create(id, node.platform_view.as_ref()) else {
            return;
        };
        let child = unsafe { Retained::<UIView>::from_raw(pointer.as_ptr().cast()) }
            .expect("platform view");
        child.setAutoresizingMask(
            UIViewAutoresizing::FlexibleWidth | UIViewAutoresizing::FlexibleHeight,
        );
        child.setFrame(self.views[&id].bounds());
        self.views[&id].addSubview(&child);
        self.hosted
            .insert(id, (node.platform_view.to_string(), child));
    }
    pub fn len(&self) -> usize {
        self.views.len()
    }
    pub fn is_empty(&self) -> bool {
        self.views.is_empty()
    }
    /// Call only after Engine::flush succeeds. Returns before UIKit draws the
    /// run-loop frame. Objective-C exceptions remain fatal, not rollback events.
    pub fn sync(&mut self, engine: &Engine) -> Result<()> {
        // Preflight every fallible engine lookup before touching presentation.
        let frames: Vec<_> = engine
            .nodes()
            .map(|(id, _)| engine.frame(id).map(|f| (id, f)))
            .collect::<Result<_>>()?;
        let virtual_parents: HashSet<u32> = engine
            .nodes()
            .filter(|(_, n)| n.virtual_list)
            .flat_map(|(id, n)| [id, n.children[0]])
            .collect();
        autoreleasepool(|_| {
            let _transaction = NoLayerAnimations::new();
            if self.mounted_root != engine.root() {
                if let Some(old) = self.views.get(&self.mounted_root) {
                    old.removeFromSuperview();
                }
            }
            for (old, new) in super::row_recycling::plan(&self.applied, engine) {
                if let Some(view) = self.views.remove(&old) {
                    self.views.insert(new, view);
                    if let Some(background) = self.backgrounds.remove(&old) {
                        self.backgrounds.insert(new, background);
                    }
                    if let Some((name, child)) = self.hosted.remove(&old) {
                        crate::platform_view::rebind(
                            old,
                            new,
                            &name,
                            Retained::as_ptr(&child).cast(),
                        );
                        self.hosted.insert(new, (name, child));
                    }
                    crate::events::rebind(old, new);
                }
            }
            crate::events::retain_bindings(|id| engine.node(id).is_some());
            let removed: Vec<u32> = self
                .views
                .keys()
                .copied()
                .filter(|id| engine.node(*id).is_none())
                .collect();
            for id in removed {
                if let Some((name, child)) = self.hosted.remove(&id) {
                    crate::platform_view::detach(id, &name, Retained::as_ptr(&child).cast());
                    child.removeFromSuperview();
                }
                if let Some(view) = self.views.remove(&id) {
                    view.removeFromSuperview();
                }
            }
            self.applied.retain(|id, _| engine.node(*id).is_some());
            self.backgrounds.retain(|id, _| engine.node(*id).is_some());
            self.frames.retain(|id, _| engine.node(*id).is_some());
            for (id, node) in engine.nodes() {
                let view = self
                    .views
                    .entry(id)
                    .or_insert_with(|| create_view(id, node, self.mtm));
                if let Some(collection) = view.downcast_ref::<collection::NativeCollection>() {
                    collection.begin_update();
                }
                if let Some(scroll) = view.downcast_ref::<scroll::NativeScroll>() {
                    scroll.begin_update();
                }
                if node.kind == Kind::BottomNavigationBar
                    && self
                        .applied
                        .get(&id)
                        .is_none_or(|old| old.bottom_navigation != node.bottom_navigation)
                {
                    view.downcast_ref::<bottom_navigation::NativeTabBar>()
                        .unwrap()
                        .configure(node);
                }
                if self.applied.get(&id).is_none_or(|old| {
                    old.text != node.text
                        || old.style != node.style
                        || old.font_revision != node.font_revision
                        || old.font_asset != node.font_asset
                        || old.font_axes != node.font_axes
                        || old.typography != node.typography
                        || old.text_runs != node.text_runs
                        || old.resolved_flow != node.resolved_flow
                        || old.extended_layout != node.extended_layout
                        || old.padding() != node.padding()
                        || old.control != node.control
                        || old.text_input != node.text_input
                        || old.image != node.image
                        || old.image_tint != node.image_tint
                }) {
                    apply_style(view, node.style);
                    if node.kind == Kind::NavigationPage {
                        // UIKit owns chrome that can extend outside the page's bounds.
                        view.layer().setMasksToBounds(false);
                    }
                    match node.kind {
                        Kind::VStack
                        | Kind::ScrollView
                        | Kind::Navigation
                        | Kind::NavigationPage
                        | Kind::NavigationHeader
                        | Kind::PlatformView => (),
                        Kind::BottomNavigationBar | Kind::DesktopNavigation => (),
                        Kind::TextField => {
                            if let Some(field) = view.downcast_ref::<text_input::NativeTextView>() {
                                field.configure(node);
                            } else {
                                let field = view.downcast_ref::<NativeTextField>().unwrap();
                                field.ivars().padding.set(node.padding().map(CGFloat::from));
                                configure_field(field, node);
                            }
                            if self
                                .applied
                                .get(&id)
                                .is_some_and(|old| old.text_input != node.text_input)
                                && view.isFirstResponder()
                            {
                                view.reloadInputViews();
                            }
                        }
                        Kind::RadioButton => configure_radio(
                            view.downcast_ref::<NativeRadio>().unwrap(),
                            node,
                            node.padding().map(CGFloat::from),
                        ),
                        Kind::Switch => {
                            let control = view.downcast_ref::<UISwitch>().unwrap();
                            control.setOn(node.control.value != 0.);
                        }
                        Kind::Slider => {
                            let control = view.downcast_ref::<UISlider>().unwrap();
                            control.setMinimumValue(node.control.minimum);
                            control.setMaximumValue(node.control.maximum);
                            control.setValue(node.control.value);
                        }
                        Kind::Image | Kind::Icon => {
                            let image = view.downcast_ref::<UIImageView>().unwrap();
                            image.setImage(Some(&decoded_image(node).expect("validated image")));
                            image.setContentMode(UIViewContentMode::ScaleAspectFit);
                            unsafe {
                                image.setTintColor(Some(&color(
                                    node.image_tint.unwrap_or(node.style.foreground),
                                )));
                            }
                        }
                        Kind::Text => {
                            let label = view
                                .downcast_ref::<PaddedLabel>()
                                .expect("registry kind invariant");
                            label.ivars().padding.set(node.padding().map(CGFloat::from));
                            configure_label(label, node);
                            label.setNeedsDisplay();
                        }
                        Kind::Button => configure_button(
                            view.downcast_ref::<UIButton>()
                                .expect("registry kind invariant"),
                            node,
                            node.padding().map(CGFloat::from),
                        ),
                    }
                }
            }
            // Detach changed lists first, so reparenting cannot temporarily create
            // a UIKit ancestor cycle when the new valid tree inverts old ancestry.
            for (id, old) in &self.applied {
                if engine
                    .node(*id)
                    .is_some_and(|new| new.children != old.children)
                {
                    if old.kind == Kind::Navigation || virtual_parents.contains(id) {
                        continue;
                    }
                    for child in &old.children {
                        if let Some(view) = self.views.get(child) {
                            view.removeFromSuperview();
                        }
                    }
                }
            }
            for (id, node) in engine.nodes() {
                if self
                    .applied
                    .get(&id)
                    .is_none_or(|old| old.children != node.children)
                {
                    if node.kind == Kind::Navigation || virtual_parents.contains(&id) {
                        continue;
                    }
                    let parent = &self.views[&id];
                    for child in &node.children {
                        parent.addSubview(&self.views[child]);
                    }
                }
            }
            for (id, node) in engine.nodes() {
                if node.kind != Kind::Navigation
                    && !virtual_parents.contains(&id)
                    && self.applied.get(&id).is_none_or(|old| {
                        old.paint_children != node.paint_children || old.children != node.children
                    })
                {
                    for child in &node.paint_children {
                        self.views[&id].bringSubviewToFront(&self.views[child]);
                    }
                }
            }
            for (id, frame) in frames {
                let node = engine.node(id).expect("frame preflight");
                let geometry_changed = self.frames.get(&id) != Some(&frame);
                if geometry_changed && node.kind != Kind::NavigationPage {
                    self.views[&id]
                        .layer()
                        .setTransform(objc2_quartz_core::CATransform3D::new_scale(1., 1., 1.));
                    self.views[&id].setFrame(CGRect::new(
                        CGPoint::new(frame.x as CGFloat, frame.y as CGFloat),
                        CGSize::new(frame.width as CGFloat, frame.height as CGFloat),
                    ));
                }
                if node.kind == Kind::PlatformView {
                    self.mount_platform_view(id, node);
                }
                if geometry_changed {
                    self.frames.insert(id, frame);
                }
                if !super::apple_box::layer_needs_sync(
                    self.applied.get(&id).map(|old| &**old),
                    node,
                    geometry_changed,
                ) {
                    continue;
                }
                self.views[&id].setHidden(node.behavior.visibility != 0);
                if node.kind == Kind::BottomNavigationBar {
                    self.views[&id].setUserInteractionEnabled(node.enabled);
                }
                if let Some(editor) = self.views[&id].downcast_ref::<text_input::NativeTextView>() {
                    editor.setEditable(node.enabled);
                    if !node.enabled {
                        editor.resignFirstResponder();
                    }
                }
                if let Some(control) = self.views[&id].downcast_ref::<objc2_ui_kit::UIControl>() {
                    control.setEnabled(node.enabled);
                }

                apply_visual(&self.views[&id].layer(), node);
                apply_gradient(&self.views[&id].layer(), node);
                super::apple_background::sync(
                    &self.views[&id].layer(),
                    node,
                    self.backgrounds.entry(id).or_default(),
                    self.applied
                        .get(&id)
                        .is_none_or(|old| old.background_image != node.background_image),
                );
                super::apple_box::apply(&self.views[&id].layer(), node);
                if node.kind == Kind::Image {
                    let image = self.views[&id].downcast_ref::<UIImageView>().unwrap();
                    let fits = image.image().is_some_and(|value| unsafe {
                        let size = value.size();
                        let bounds = image.bounds().size;
                        size.width <= bounds.width && size.height <= bounds.height
                    });
                    image.setContentMode(match node.image_fit {
                        1 => UIViewContentMode::ScaleAspectFill,
                        2 => UIViewContentMode::ScaleToFill,
                        3 => UIViewContentMode::Center,
                        4 if fits => UIViewContentMode::Center,
                        _ => UIViewContentMode::ScaleAspectFit,
                    });
                }
            }
            for (id, node) in engine.nodes() {
                if node.virtual_list {
                    self.views[&id]
                        .downcast_ref::<collection::NativeCollection>()
                        .unwrap()
                        .configure(engine, &self.views, node, !self.applied.contains_key(&id));
                } else if node.kind == Kind::ScrollView {
                    let extent = node
                        .children
                        .iter()
                        .filter_map(|id| engine.frame(*id).ok())
                        .fold(CGSize::ZERO, |size, f| {
                            CGSize::new(
                                size.width.max((f.x + f.width) as CGFloat),
                                size.height.max((f.y + f.height) as CGFloat),
                            )
                        });
                    let scroll = self.views[&id].downcast_ref::<UIScrollView>().unwrap();
                    let frame = engine.frame(id).expect("committed scroll frame");
                    let horizontal = node.scroll_axis == 1;
                    let inset = scroll.adjustedContentInset();
                    let viewport = CGSize::new(
                        (frame.width as CGFloat - inset.left - inset.right).max(0.),
                        (frame.height as CGFloat - inset.top - inset.bottom).max(0.),
                    );
                    if self
                        .applied
                        .get(&id)
                        .is_none_or(|old| old.scroll_axis != node.scroll_axis)
                    {
                        scroll.setShowsHorizontalScrollIndicator(horizontal);
                        scroll.setShowsVerticalScrollIndicator(!horizontal);
                        scroll.setAlwaysBounceHorizontal(false);
                        scroll.setAlwaysBounceVertical(false);
                    }
                    let content_size = if horizontal {
                        CGSize::new(extent.width.max(viewport.width), viewport.height)
                    } else {
                        CGSize::new(viewport.width, extent.height.max(viewport.height))
                    };
                    if scroll.contentSize() != content_size {
                        scroll.setContentSize(content_size);
                    }
                    if self
                        .applied
                        .get(&id)
                        .is_some_and(|old| old.scroll_axis != node.scroll_axis)
                    {
                        scroll.setContentOffset(CGPoint::new(-inset.left, -inset.top));
                    }
                    scroll
                        .downcast_ref::<scroll::NativeScroll>()
                        .unwrap()
                        .configure(node, !self.applied.contains_key(&id));
                }
                if node.kind == Kind::TextField
                    && node.control.focus_revision > 0
                    && self
                        .applied
                        .get(&id)
                        .is_none_or(|old| old.control.focus_revision != node.control.focus_revision)
                {
                    if node.control.focus_requested {
                        self.views[&id].becomeFirstResponder();
                    } else {
                        self.views[&id].resignFirstResponder();
                    }
                }
            }
            drop(_transaction);
            for (id, node) in engine.nodes() {
                if node.kind == Kind::Navigation {
                    let view = self.views[&id]
                        .downcast_ref::<navigation::NavigationView>()
                        .unwrap();
                    view.sync(engine, &self.views, node);
                }
            }
            if let Some(root) = engine.node(engine.root()) {
                let background = color(root.style.background);
                self.host.setBackgroundColor(Some(&background));
                if let Some(parent) = self.host.superview() {
                    parent.setBackgroundColor(Some(&background));
                }
                if let Some(window) = self.host.window() {
                    window.setBackgroundColor(Some(&background));
                    if root.kind == Kind::Navigation {
                        let mode = root.control.focus_revision as isize;
                        unsafe {
                            let current: isize = msg_send![&window, overrideUserInterfaceStyle];
                            if current != mode {
                                let _: () = msg_send![&window, setOverrideUserInterfaceStyle: mode];
                            }
                        }
                    }
                }
            }
            if engine.root() != 0 && self.mounted_root != engine.root() {
                self.host.addSubview(&self.views[&engine.root()]);
            }
            self.mounted_root = engine.root();
            self.applied = engine.presentation_nodes();
        });
        Ok(())
    }
}
impl Drop for NativeRegistry {
    fn drop(&mut self) {
        for view in self.views.values() {
            view.removeFromSuperview();
        }
    }
}
struct NoLayerAnimations;
impl NoLayerAnimations {
    fn new() -> Self {
        CATransaction::begin();
        CATransaction::setDisableActions(true);
        Self
    }
}
impl Drop for NoLayerAnimations {
    fn drop(&mut self) {
        CATransaction::commit();
    }
}

/// Detached reusable UIKit prototypes: never mutate committed visible controls
/// while Taffy explores a candidate transaction. NSString copies preserve UTF-8
/// conversion and embedded NULs; no borrowed command-buffer memory is retained.
pub struct NativeMeasurer {
    radio: Option<Retained<NativeRadio>>,
    field: Option<Retained<UITextField>>,
    multiline: Option<Retained<text_input::NativeTextView>>,
    toggle: Option<Retained<UISwitch>>,
    slider: Option<Retained<UISlider>>,
    tabs: Option<Retained<bottom_navigation::NativeTabBar>>,
    host: Retained<UIView>,
    label: Retained<UILabel>,
    button: Retained<UIButton>,
}
impl NativeMeasurer {
    pub fn new(mtm: MainThreadMarker, host: Retained<UIView>) -> Self {
        Self {
            host,
            radio: None,
            field: None,
            multiline: None,
            toggle: None,
            slider: None,
            tabs: None,
            label: UILabel::initWithFrame(UILabel::alloc(mtm), CGRect::ZERO),
            button: UIButton::buttonWithType(UIButtonType::Custom, mtm),
        }
    }
}
impl Measure for NativeMeasurer {
    fn bottom_safe_area(&self) -> f32 {
        self.host.safeAreaInsets().bottom as f32
    }
    fn validate_style(&self, node: &Node) -> Result<()> {
        if node.kind == Kind::DesktopNavigation {
            return Err(Error::Tree(
                "native desktop navigation requires macOS or Windows",
            ));
        }
        super::apple_fonts::register(node)
    }
    fn navigation_bar_height(&mut self, width: f32) -> f32 {
        navigation::bar_height(width, self.label.mtm())
    }
    fn measure(
        &mut self,
        node: &Node,
        known: Size<Option<f32>>,
        available: Size<AvailableSpace>,
    ) -> Result<Size<f32>> {
        autoreleasepool(|_| {
            let constraint = CGSize::new(
                axis(known.width, available.width),
                axis(known.height, available.height),
            );
            let measured = match node.kind {
                Kind::Text => {
                    configure_label(&self.label, node);
                    self.label.sizeThatFits(constraint)
                }
                Kind::Button => {
                    configure_button(&self.button, node, [0.; 4]);
                    self.button.sizeThatFits(constraint)
                }
                Kind::TextField if node.text_input.multiline() => {
                    let field = self.multiline.get_or_insert_with(|| {
                        text_input::NativeTextView::new(0, self.label.mtm())
                    });
                    text_input::configure(field, node);
                    let size = field.sizeThatFits(constraint);
                    CGSize::new(size.width.max(120.), size.height.max(64.))
                }
                Kind::TextField => {
                    let field = self.field.get_or_insert_with(|| {
                        UITextField::initWithFrame(
                            UITextField::alloc(self.label.mtm()),
                            CGRect::ZERO,
                        )
                    });
                    configure_field(field, node);
                    let size = field.sizeThatFits(constraint);
                    CGSize::new(size.width.max(120.), size.height.max(32.))
                }
                Kind::RadioButton => {
                    let radio = self
                        .radio
                        .get_or_insert_with(|| NativeRadio::new(0, self.label.mtm()));
                    configure_radio(radio, node, [0.; 4]);
                    let size = radio.sizeThatFits(constraint);
                    CGSize::new(
                        size.width,
                        size.height.max(
                            (44. - (node.padding()[1] + node.padding()[3]) as CGFloat).max(0.),
                        ),
                    )
                }
                Kind::Switch => self
                    .toggle
                    .get_or_insert_with(|| {
                        UISwitch::initWithFrame(UISwitch::alloc(self.label.mtm()), CGRect::ZERO)
                    })
                    .sizeThatFits(constraint),
                Kind::Slider => self
                    .slider
                    .get_or_insert_with(|| {
                        UISlider::initWithFrame(UISlider::alloc(self.label.mtm()), CGRect::ZERO)
                    })
                    .sizeThatFits(CGSize::new(constraint.width.min(120.), constraint.height)),
                Kind::BottomNavigationBar => self
                    .tabs
                    .get_or_insert_with(|| {
                        bottom_navigation::NativeTabBar::new(0, self.label.mtm())
                    })
                    .sizeThatFits(constraint),
                Kind::Image | Kind::Icon => unsafe { decoded_image(node)?.size() },
                Kind::PlatformView => CGSize::ZERO,
                Kind::VStack
                | Kind::DesktopNavigation
                | Kind::ScrollView
                | Kind::Navigation
                | Kind::NavigationPage
                | Kind::NavigationHeader => {
                    return Err(Error::Measurement(
                        "container requested native leaf measurement",
                    ))
                }
            };
            let size = Size {
                width: known.width.unwrap_or(measured.width as f32),
                height: known.height.unwrap_or(measured.height as f32),
            };
            if !size.width.is_finite()
                || !size.height.is_finite()
                || size.width < 0.
                || size.height < 0.
            {
                return Err(Error::Measurement("UIKit returned an invalid size"));
            }
            Ok(size)
        })
    }
}
fn axis(known: Option<f32>, available: AvailableSpace) -> CGFloat {
    if let Some(value) = known {
        return value as CGFloat;
    }
    match available {
        AvailableSpace::Definite(value) => value.max(0.) as CGFloat,
        AvailableSpace::MinContent => 0.,
        AvailableSpace::MaxContent => CGFloat::MAX,
    }
}

thread_local! {
    static REGISTRY: std::cell::RefCell<Option<NativeRegistry>> = const { std::cell::RefCell::new(None) };
}

/// Runner setup before any C# flush. The host's bounds must match SET_VIEWPORT;
/// safe-area placement is the runner's responsibility. Button actions enqueue node IDs for the managed display tick.
pub fn initialize(host: Retained<UIView>, mtm: MainThreadMarker) -> Result<()> {
    crate::initialize(Box::new(NativeMeasurer::new(mtm, host.clone())))?;
    REGISTRY.with(|slot| *slot.borrow_mut() = Some(NativeRegistry::new(host, mtm)));
    Ok(())
}

pub(crate) fn flush(bytes: &[u8]) -> Result<()> {
    REGISTRY.with(|registry| {
        let mut registry = registry
            .try_borrow_mut()
            .map_err(|_| Error::Tree("reentrant UIKit presentation"))?;
        let registry = registry
            .as_mut()
            .ok_or(Error::Tree("initialize iOS host before flushing"))?;
        crate::ENGINE.with(|engine| {
            let mut engine = engine
                .try_borrow_mut()
                .map_err(|_| Error::Tree("reentrant engine access"))?;
            let engine = engine.as_mut().ok_or(Error::Tree("missing iOS engine"))?;
            engine.flush(bytes)?;
            engine.report_layouts()?;
            registry.sync(engine)
        })
    })
}

/// Initialize the process-lifetime iOS session from an Objective-C runner.
///
/// # Safety
/// `host` must be a live UIView on the OS main thread. It is borrowed here and
/// retained for the session. No managed object or video buffer crosses this API.
#[no_mangle]
pub unsafe extern "C" fn dotnative_ios_initialize(host: *mut UIView) -> i32 {
    // The panic path aborts immediately; no potentially inconsistent UIKit state
    // is observed after unwinding. This assertion is not used for recovery.
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> Result<()> {
        let mtm = MainThreadMarker::new()
            .ok_or(Error::Tree("iOS initialization requires main thread"))?;
        // SAFETY: caller guarantees a live UIView; retain returns None for nil.
        let host = unsafe { Retained::retain(host) }.ok_or(Error::Tree("nil iOS host"))?;
        initialize(host, mtm)
    }));
    match result {
        Ok(Ok(())) => 0,
        Ok(Err(error)) => {
            eprintln!("DotNative initialization failed: {error}");
            -1
        }
        Err(_) => {
            eprintln!("DotNative initialization panic");
            std::process::abort();
        }
    }
}
