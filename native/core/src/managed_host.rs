//! Versioned in-process callback bridge for Mono/CoreCLR application hosts.
use std::sync::OnceLock;

#[repr(C)]
#[derive(Clone, Copy)]
pub struct Callbacks {
    start: Option<unsafe extern "C" fn(f32, f32) -> i32>,
    resize: Option<unsafe extern "C" fn(f32, f32) -> i32>,
    tick: Option<unsafe extern "C" fn() -> i32>,
    stop: Option<unsafe extern "C" fn() -> i32>,
    activity: Option<unsafe extern "C" fn(i32) -> i32>,
    theme: Option<unsafe extern "C" fn(i32) -> i32>,
    accessibility: Option<unsafe extern "C" fn(i32, f32, i32) -> i32>,
    insets: Option<unsafe extern "C" fn(f32, f32, f32) -> i32>,
}

static CALLBACKS: OnceLock<Callbacks> = OnceLock::new();

/// The runtime must keep all callback targets alive for the process lifetime.
#[no_mangle]
pub unsafe extern "C" fn dotnative_register_managed_host(
    version: u32,
    size: usize,
    callbacks: *const Callbacks,
) -> i32 {
    // Version 2 adds the window-insets callback. Host and runtime ship together.
    if version != 2 || size != std::mem::size_of::<Callbacks>() || callbacks.is_null() {
        return -1;
    }
    let callbacks = unsafe { *callbacks };
    if callbacks.start.is_none()
        || callbacks.resize.is_none()
        || callbacks.tick.is_none()
        || callbacks.stop.is_none()
        || callbacks.activity.is_none()
        || callbacks.theme.is_none()
        || callbacks.accessibility.is_none()
        || callbacks.insets.is_none()
    {
        return -1;
    }
    CALLBACKS.set(callbacks).map_or(-1, |_| 0)
}

#[no_mangle]
pub extern "C" fn dotnative_app_start(width: f32, height: f32) -> i32 {
    CALLBACKS
        .get()
        .map_or(-1, |c| unsafe { (c.start.unwrap())(width, height) })
}
#[no_mangle]
pub extern "C" fn dotnative_app_resize(width: f32, height: f32) -> i32 {
    CALLBACKS
        .get()
        .map_or(-1, |c| unsafe { (c.resize.unwrap())(width, height) })
}
#[no_mangle]
pub extern "C" fn dotnative_app_insets(top: f32, bottom: f32, keyboard: f32) -> i32 {
    CALLBACKS.get().map_or(-1, |c| unsafe {
        (c.insets.unwrap())(top, bottom, keyboard)
    })
}
#[no_mangle]
pub extern "C" fn dotnative_app_tick() -> i32 {
    CALLBACKS
        .get()
        .map_or(-1, |c| unsafe { (c.tick.unwrap())() })
}
#[no_mangle]
pub extern "C" fn dotnative_app_stop() -> i32 {
    CALLBACKS
        .get()
        .map_or(-1, |c| unsafe { (c.stop.unwrap())() })
}
#[no_mangle]
pub extern "C" fn dotnative_app_activity(activity: i32) -> i32 {
    CALLBACKS
        .get()
        .map_or(-1, |c| unsafe { (c.activity.unwrap())(activity) })
}
#[no_mangle]
pub extern "C" fn dotnative_app_system_theme(theme: i32) -> i32 {
    CALLBACKS
        .get()
        .map_or(-1, |c| unsafe { (c.theme.unwrap())(theme) })
}
#[no_mangle]
pub extern "C" fn dotnative_app_accessibility(motion: i32, scale: f32, contrast: i32) -> i32 {
    CALLBACKS.get().map_or(-1, |c| unsafe {
        (c.accessibility.unwrap())(motion, scale, contrast)
    })
}
