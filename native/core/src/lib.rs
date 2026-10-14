//! DotNative: binary commands, Flexbox layout and native platform presentation.
// Rust 1.94's Android TLS macro emits an internal initializer that triggers this
// lint even though our thread_local declarations already use const initializers.
#![cfg_attr(target_os = "android", allow(clippy::missing_const_for_thread_local))]
#[cfg(all(feature = "dev-host", feature = "managed-host"))]
compile_error!("Use either dev-host or managed-host, not both.");
#[cfg(any(test, not(target_os = "android")))]
mod box_geometry;
#[cfg(feature = "dev-host")]
mod dev_host;
mod engine;
mod events;
mod gradients;
mod image_fit;
#[cfg(feature = "managed-host")]
mod managed_host;
mod platform_view;
mod plugins;
pub use events::dotnative_poll_click;
pub mod platform;
mod protocol;
pub use engine::{Engine, Frame, Measure, Node, RequireNativeMeasure};
pub use protocol::{Kind, LayoutStyle, Style};
use std::{cell::RefCell, sync::OnceLock, thread::ThreadId};
pub use taffy::{AvailableSpace, Size};

#[derive(Debug)]
pub enum Error {
    Wire(&'static str),
    Tree(&'static str),
    Measurement(&'static str),
    Layout(taffy::TaffyError),
}
pub type Result<T> = std::result::Result<T, Error>;
impl From<taffy::TaffyError> for Error {
    fn from(e: taffy::TaffyError) -> Self {
        Self::Layout(e)
    }
}
impl std::fmt::Display for Error {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{self:?}")
    }
}
impl std::error::Error for Error {}
static OWNER: OnceLock<ThreadId> = OnceLock::new();
thread_local! { static ENGINE: RefCell<Option<Engine>> = const { RefCell::new(None) }; }

/// Runner calls this once on the OS main thread, before the first C# flush.
/// On iOS use platform::ios::initialize to install both measurer and registry.
pub fn initialize(measure: Box<dyn Measure>) -> Result<()> {
    bind_thread()?;
    ENGINE.with(|slot| {
        let mut slot = slot
            .try_borrow_mut()
            .map_err(|_| Error::Tree("reentrant engine access"))?;
        if slot.is_some() {
            return Err(Error::Tree("session already initialized"));
        }
        *slot = Some(Engine::new(measure));
        Ok(())
    })
}
fn bind_thread() -> Result<()> {
    #[cfg(any(target_os = "ios", target_os = "macos"))]
    if objc2::MainThreadMarker::new().is_none() {
        return Err(Error::Tree("UIKit requires the OS main thread"));
    }
    let current = std::thread::current().id();
    if *OWNER.get_or_init(|| current) != current {
        return Err(Error::Tree("engine accessed from another thread"));
    }
    Ok(())
}
/// Synchronous C ABI. Return means successful validation and commit.
///
/// # Safety
/// `buffer` must point to `len` initialized, readable bytes in a single allocation,
/// stable and not mutated until this function returns. Called on the runner's OS
/// main thread. Pointer provenance/liveness cannot be checked by Rust. No pointer
/// or borrowed text escapes this call. Errors abort because v1 has no status return.
#[no_mangle]
pub unsafe extern "C" fn flush_commands(buffer: *const u8, len: usize) {
    let outcome = std::panic::catch_unwind(|| -> Result<()> {
        if buffer.is_null() || !(16..=protocol::MAX_BYTES).contains(&len) {
            return Err(Error::Wire("invalid buffer pointer or length"));
        }
        bind_thread()?;
        // SAFETY: caller guarantees validity; non-null and bounded length checked.
        let bytes = unsafe { std::slice::from_raw_parts(buffer, len) };
        #[cfg(target_os = "ios")]
        {
            platform::ios::flush(bytes)
        }
        #[cfg(target_os = "macos")]
        {
            platform::macos::flush(bytes)
        }
        #[cfg(target_os = "android")]
        {
            platform::android::flush(bytes)
        }
        #[cfg(all(feature = "desktop", any(target_os = "linux", target_os = "windows")))]
        {
            platform::desktop::flush(bytes)
        }
        #[cfg(not(any(
            target_os = "ios",
            target_os = "macos",
            target_os = "android",
            all(feature = "desktop", any(target_os = "linux", target_os = "windows"))
        )))]
        ENGINE.with(|slot| {
            let mut slot = slot
                .try_borrow_mut()
                .map_err(|_| Error::Tree("reentrant flush"))?;
            slot.get_or_insert_with(|| Engine::new(Box::new(RequireNativeMeasure)))
                .flush(bytes)
        })
    });
    match outcome {
        Ok(Ok(())) => (),
        Ok(Err(error)) => {
            eprintln!("DotNative fatal: {error}");
            std::process::abort();
        }
        Err(_) => {
            eprintln!("DotNative fatal: panic at FFI boundary");
            std::process::abort();
        }
    }
}

#[cfg(test)]
mod tests;

mod font_assets;
