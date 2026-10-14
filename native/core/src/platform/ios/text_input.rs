use super::*;
use objc2::runtime::AnyObject;
use objc2_foundation::NSRange;
use objc2_ui_kit::{
    UITextContentTypeEmailAddress, UITextContentTypeNewPassword, UITextContentTypeOneTimeCode,
    UITextContentTypePassword, UITextContentTypeUsername, UITextView,
};

pub(super) fn traits(field: &AnyObject, node: &Node) {
    let input = node.text_input;
    unsafe {
        // UIKit forwards optional UITextInputTraits accessors. objc2's debug
        // verifier requires a concrete Method and rejects legitimate forwarded
        // messages. Keep the exact UIKit ABI here; all other view calls stay typed.
        let set_bool: unsafe extern "C-unwind" fn(
            *const AnyObject,
            objc2::runtime::Sel,
            objc2::runtime::Bool,
        ) = std::mem::transmute(objc2::ffi::objc_msgSend as *const ());
        let set_integer: unsafe extern "C-unwind" fn(
            *const AnyObject,
            objc2::runtime::Sel,
            NSInteger,
        ) = std::mem::transmute(objc2::ffi::objc_msgSend as *const ());
        let set_string: unsafe extern "C-unwind" fn(
            *const AnyObject,
            objc2::runtime::Sel,
            *const NSString,
        ) = std::mem::transmute(objc2::ffi::objc_msgSend as *const ());
        let get_bool: unsafe extern "C-unwind" fn(
            *const AnyObject,
            objc2::runtime::Sel,
        ) -> objc2::runtime::Bool = std::mem::transmute(objc2::ffi::objc_msgSend as *const ());
        if get_bool(field, sel!(isSecureTextEntry)).as_bool() != input.password() {
            set_bool(
                field,
                sel!(setSecureTextEntry:),
                objc2::runtime::Bool::new(input.password()),
            );
        }
        let keyboard: NSInteger = if input.password() {
            0
        } else {
            [0, 7, 4, 8, 5, 3][input.keyboard as usize]
        };
        let action: NSInteger = [0, 9, 4, 6, 7, 1][input.action as usize];
        let capitalization: NSInteger = if input.password() {
            0
        } else {
            [0, 2, 1, 3][input.capitalization as usize]
        };
        let correction: NSInteger = if input.autocorrect() { 2 } else { 1 };
        let content: Option<&NSString> = match input.autofill {
            1 => Some(UITextContentTypeUsername),
            2 => Some(UITextContentTypePassword),
            3 => Some(UITextContentTypeNewPassword),
            4 => Some(UITextContentTypeEmailAddress),
            5 => Some(UITextContentTypeOneTimeCode),
            _ => None,
        };
        set_integer(field, sel!(setKeyboardType:), keyboard);
        set_integer(field, sel!(setReturnKeyType:), action);
        set_integer(field, sel!(setAutocapitalizationType:), capitalization);
        set_integer(field, sel!(setAutocorrectionType:), correction);
        set_integer(field, sel!(setSpellCheckingType:), correction);
        set_string(
            field,
            sel!(setTextContentType:),
            content.map_or(std::ptr::null(), |s| s as *const NSString),
        );
    }
}

pub(super) struct MultilineIvars {
    id: u32,
    action: Cell<u8>,
    placeholder: Retained<UILabel>,
}
define_class!(
    #[unsafe(super(UITextView))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeTextView"]
    #[ivars=MultilineIvars]
    pub(super) struct NativeTextView;
    impl NativeTextView {
        #[unsafe(method(textViewDidChange:))]
        fn changed(&self, _sender: &UITextView) {
            self.ivars().placeholder.setHidden(!self.text().is_empty());
            crate::events::emit(self.ivars().id, 2, 0., self.text().to_string());
        }
        #[unsafe(method(textViewDidBeginEditing:))]
        fn focused(&self, _sender: &UITextView) { crate::events::emit(self.ivars().id, 4, 1., String::new()); }
        #[unsafe(method(textViewDidEndEditing:))]
        fn blurred(&self, _sender: &UITextView) { crate::events::emit(self.ivars().id, 4, 0., String::new()); }
        #[unsafe(method(textView:shouldChangeTextInRange:replacementText:))]
        fn should_change(&self, _sender: &UITextView, _range: NSRange, text: &NSString) -> bool {
            let marked: Option<Retained<AnyObject>> = unsafe { msg_send![self, markedTextRange] };
            if self.ivars().action.get() != 0 && text.to_string() == "\n" && marked.is_none() {
                crate::events::emit(self.ivars().id, 9, 0., self.text().to_string());
                false
            } else { true }
        }
        #[unsafe(method(layoutSubviews))]
        fn layout_subviews(&self) {
            unsafe { let _: () = msg_send![super(self), layoutSubviews]; }
            let inset = self.textContainerInset();
            let label = &self.ivars().placeholder;
            let width = (self.bounds().size.width - inset.left - inset.right).max(0.);
            let height = label.sizeThatFits(CGSize::new(width, CGFloat::MAX)).height;
            label.setFrame(CGRect::new(CGPoint::new(inset.left, inset.top), CGSize::new(width, height)));
        }
    }
);
impl NativeTextView {
    pub(super) fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let label = UILabel::initWithFrame(UILabel::alloc(mtm), CGRect::ZERO);
        label.setNumberOfLines(0);
        label.setUserInteractionEnabled(false);
        unsafe {
            let _: () = msg_send![&label, setIsAccessibilityElement: false];
        }
        let allocated = Self::alloc(mtm).set_ivars(MultilineIvars {
            id,
            action: Cell::new(0),
            placeholder: label,
        });
        let view: Retained<Self> =
            unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] };
        unsafe {
            let _: () = msg_send![&view, setDelegate: &*view];
        }
        view.addSubview(&view.ivars().placeholder);
        view
    }
    pub(super) fn configure(&self, node: &Node) {
        configure(self, node);
        self.ivars().action.set(node.text_input.action);
        let [left, top, right, bottom] = node.padding().map(CGFloat::from);
        self.setTextContainerInset(UIEdgeInsets {
            top,
            left,
            bottom,
            right,
        });
        let label = &self.ivars().placeholder;
        label.setText(Some(&NSString::from_str(&node.control.placeholder)));
        unsafe {
            label.setFont(Some(&styled_font(node)));
        }
        unsafe {
            label.setTextColor(Some(&color(node.text_input.placeholder_color.unwrap_or([
                node.style.foreground[0],
                node.style.foreground[1],
                node.style.foreground[2],
                150,
            ]))));
        }
        label.setTextAlignment(text_alignment(node));
        label.setHidden(!self.text().is_empty());
        self.setNeedsLayout();
    }
}
#[allow(deprecated)] // selectedRange is available on every supported iOS version.
pub(super) fn configure(view: &UITextView, node: &Node) {
    traits(view, node);
    let marked: Option<Retained<AnyObject>> = unsafe { msg_send![view, markedTextRange] };
    if view.text().to_string() != *node.text && marked.is_none() {
        let selection = view.selectedRange();
        view.setText(Some(&NSString::from_str(&node.text)));
        let length = node.text.encode_utf16().count();
        let start = selection.location.min(length);
        view.setSelectedRange(NSRange::new(start, selection.length.min(length - start)));
    }
    view.setFont(Some(&styled_font(node)));
    view.setTextColor(Some(&color(node.style.foreground)));
    view.setTextAlignment(text_alignment(node));
    view.setTextContainerInset(UIEdgeInsets {
        top: 0.,
        left: 0.,
        bottom: 0.,
        right: 0.,
    });
    unsafe {
        let container: Retained<AnyObject> = msg_send![view, textContainer];
        let _: () = msg_send![&container, setLineFragmentPadding: 0.0 as CGFloat];
    }
}
