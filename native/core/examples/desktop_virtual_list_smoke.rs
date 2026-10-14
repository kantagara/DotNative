#[cfg(all(target_os = "macos", feature = "dev-host"))]
fn main() {
    use objc2::{class, msg_send};
    // AppKit controls require an application object even in a headless fixture.
    unsafe {
        let _: *mut objc2::runtime::AnyObject = msg_send![class!(NSApplication), sharedApplication];
    }
    core::platform::macos::run_virtual_list_smoke();
}
#[cfg(not(all(target_os = "macos", feature = "dev-host")))]
fn main() {
    eprintln!("macOS: run with --features dev-host; Linux/Windows: dotnative-desktop --smoke");
}
