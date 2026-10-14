//! cargo build --target aarch64-apple-ios-sim --example ios_input_smoke
//! xcrun simctl spawn booted <absolute path to executable>
#[cfg(not(target_os = "ios"))]
fn main() {
    eprintln!("Requires iOS Simulator");
}

#[cfg(target_os = "ios")]
fn main() {
    use core::{
        platform::ios::{NativeMeasurer, NativeRegistry},
        Engine,
    };
    use objc2::{msg_send, sel, MainThreadMarker, MainThreadOnly};
    use objc2_core_foundation::{CGPoint, CGRect, CGSize};
    use objc2_foundation::{NSRange, NSString};
    use objc2_ui_kit::{UITextField, UITextView, UIView};
    unsafe extern "C" {
        fn dotnative_poll_event(buffer: *mut u8, capacity: usize) -> usize;
    }
    fn frame(ops: &[(u8, Vec<u8>)]) -> Vec<u8> {
        let mut bytes = b"DNTV\x01\x00\x10\x00".to_vec();
        bytes.extend(0u32.to_le_bytes());
        bytes.extend((ops.len() as u32).to_le_bytes());
        for (kind, payload) in ops {
            bytes.push(*kind);
            bytes.extend((payload.len() as u32).to_le_bytes());
            bytes.extend(payload);
        }
        let len = bytes.len() as u32;
        bytes[8..12].copy_from_slice(&len.to_le_bytes());
        bytes
    }
    fn text(id: u32, value: &str) -> Vec<u8> {
        let mut p = id.to_le_bytes().to_vec();
        p.extend((value.len() as u32).to_le_bytes());
        p.extend(value.as_bytes());
        p
    }
    fn trait_value(view: &UIView, selector: objc2::runtime::Sel) -> isize {
        let send: unsafe extern "C-unwind" fn(*const UIView, objc2::runtime::Sel) -> isize =
            unsafe { std::mem::transmute(objc2::ffi::objc_msgSend as *const ()) };
        unsafe { send(view, selector) }
    }
    fn secure(view: &UIView) -> bool {
        let send: unsafe extern "C-unwind" fn(
            *const UIView,
            objc2::runtime::Sel,
        ) -> objc2::runtime::Bool =
            unsafe { std::mem::transmute(objc2::ffi::objc_msgSend as *const ()) };
        unsafe { send(view, sel!(isSecureTextEntry)).as_bool() }
    }
    fn event(id: u32, value: &str) {
        let mut bytes = [0u8; 4096];
        let n = unsafe { dotnative_poll_event(bytes.as_mut_ptr(), bytes.len()) };
        assert_eq!(n, 16 + value.len());
        assert_eq!(u32::from_le_bytes(bytes[..4].try_into().unwrap()), id);
        assert_eq!(u32::from_le_bytes(bytes[4..8].try_into().unwrap()), 9);
        assert_eq!(&bytes[16..n], value.as_bytes());
    }
    let mtm = MainThreadMarker::new().unwrap();
    let host = UIView::initWithFrame(
        UIView::alloc(mtm),
        CGRect::new(CGPoint::ZERO, CGSize::new(390., 844.)),
    );
    let mut engine = Engine::new(Box::new(NativeMeasurer::new(mtm, host.clone())));
    let mut registry = NativeRegistry::new(host, mtm);
    let mut viewport = 390f32.to_le_bytes().to_vec();
    viewport.extend(844f32.to_le_bytes());
    engine
        .flush(&frame(&[
            (1, vec![1, 0, 0, 0, 1]),
            (1, vec![2, 0, 0, 0, 5]),
            (1, vec![3, 0, 0, 0, 5]),
            (2, text(2, "secret🙂")),
            (2, text(3, "first\nsecond")),
            (34, vec![2, 0, 0, 0, 0, 1, 0, 2, 1]),
            (34, vec![3, 0, 0, 0, 0, 4, 1, 0, 6]),
            (4, vec![1, 0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0]),
            (5, vec![1, 0, 0, 0]),
            (7, viewport),
        ]))
        .unwrap();
    registry.sync(&engine).unwrap();
    {
        let password = registry
            .view(2)
            .unwrap()
            .downcast_ref::<UITextField>()
            .unwrap();
        assert!(secure(password));
        assert_eq!(trait_value(password, sel!(returnKeyType)), 9);
        assert_eq!(password.text().unwrap().to_string(), "secret🙂");
        // This executable has no UIApplication event loop; invoke the registered target selector.
        unsafe {
            let _: () = msg_send![password, dotnativeSubmit: password];
        }
        event(2, "secret🙂");
        let multiline = registry
            .view(3)
            .unwrap()
            .downcast_ref::<UITextView>()
            .unwrap();
        assert_eq!(multiline.text().to_string(), "first\nsecond");
        assert!(multiline.frame().size.height >= 64.);
        let accepted: bool = unsafe {
            msg_send![multiline,textView:multiline, shouldChangeTextInRange:NSRange::new(12,0), replacementText:&*NSString::from_str("\n")]
        };
        assert!(!accepted);
        event(3, "first\nsecond");
    }
    let identity = registry.view(2).unwrap() as *const UIView;
    engine
        .flush(&frame(&[
            (34, vec![2, 0, 0, 0, 1, 2, 0, 4, 4]),
            (34, vec![3, 0, 0, 0, 0, 0, 0, 0, 6]),
        ]))
        .unwrap();
    registry.sync(&engine).unwrap();
    assert_eq!(identity, registry.view(2).unwrap() as *const UIView);
    let field = registry
        .view(2)
        .unwrap()
        .downcast_ref::<UITextField>()
        .unwrap();
    assert!(!secure(field));
    assert_eq!(trait_value(field, sel!(keyboardType)), 7);
    assert_eq!(trait_value(field, sel!(returnKeyType)), 4);
    assert_eq!(field.text().unwrap().to_string(), "secret🙂");
    let multiline = registry
        .view(3)
        .unwrap()
        .downcast_ref::<UITextView>()
        .unwrap();
    let accepted: bool = unsafe {
        msg_send![multiline,textView:multiline, shouldChangeTextInRange:NSRange::new(12,0), replacementText:&*NSString::from_str("\n")]
    };
    assert!(accepted);
    engine
        .flush(&frame(&[
            (34, vec![2, 0, 0, 0, 0, 0, 0, 0, 4]),
            (27, vec![3, 0, 0, 0, 0]),
        ]))
        .unwrap();
    registry.sync(&engine).unwrap();
    let field = registry
        .view(2)
        .unwrap()
        .downcast_ref::<UITextField>()
        .unwrap();
    assert_eq!(trait_value(field, sel!(keyboardType)), 0);
    assert_eq!(trait_value(field, sel!(returnKeyType)), 0);
    assert!(!registry
        .view(3)
        .unwrap()
        .downcast_ref::<UITextView>()
        .unwrap()
        .isEditable());
    println!("PASS UIKit password, keyboard/action resets, submit events, multiline newline/send, disabled editor and identity");
}
