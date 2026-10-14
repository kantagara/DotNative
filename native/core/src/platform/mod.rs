#[cfg(target_os = "ios")]
pub mod ios;

#[cfg(target_os = "android")]
pub mod android;
#[cfg(target_os = "macos")]
pub mod macos;

#[cfg(all(feature = "desktop", any(target_os = "linux", target_os = "windows")))]
pub mod desktop;

#[cfg(any(target_os = "macos", target_os = "ios"))]
mod apple_background;

#[cfg(any(target_os = "macos", target_os = "ios"))]
mod apple_box;

#[cfg(any(target_os = "ios", target_os = "macos"))]
mod apple_fonts;

#[cfg(any(
    test,
    all(target_os = "windows", feature = "desktop", feature = "dev-host")
))]
mod frame_pump;

#[cfg(any(
    test,
    target_os = "macos",
    all(feature = "desktop", target_os = "linux")
))]
mod cell_pool;

#[cfg(all(feature = "dev-host", any(target_os = "macos", target_os = "linux")))]
mod virtual_list_smoke;

#[cfg(any(
    test,
    target_os = "ios",
    target_os = "android",
    target_os = "macos",
    all(feature = "desktop", target_os = "linux")
))]
pub(crate) mod row_recycling;
