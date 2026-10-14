//! WinUI 3 owns the complete Windows UI. Win32 only owns the top-level HWND,
//! DPI notifications and message pump; no Win32 child controls are created.
#[cfg(feature = "dev-host")]
mod frame_clock;
mod winui;
use crate::{AvailableSpace, Engine, Error, Frame, Kind, Measure, Node, Result, Size};
use std::{
    cell::RefCell,
    collections::HashMap,
    ptr::{null, null_mut},
};
use windows_sys::Win32::{
    Foundation::*,
    System::LibraryLoader::GetModuleHandleW,
    UI::{HiDpi::*, WindowsAndMessaging::*},
};
#[cfg(feature = "dev-host")]
use windows_sys::Win32::{
    System::Registry::{RegGetValueW, HKEY_CURRENT_USER, RRF_RT_REG_DWORD},
    UI::Accessibility::{HCF_HIGHCONTRASTON, HIGHCONTRASTW},
};
static UI_HOST: std::sync::atomic::AtomicIsize = std::sync::atomic::AtomicIsize::new(0);
#[cfg(feature = "dev-host")]
fn system_text_scale() -> f32 {
    let mut percent = 100u32;
    let mut bytes = std::mem::size_of_val(&percent) as u32;
    let result = unsafe {
        RegGetValueW(
            HKEY_CURRENT_USER,
            wide("Software\\Microsoft\\Accessibility").as_ptr(),
            wide("TextScaleFactor").as_ptr(),
            RRF_RT_REG_DWORD,
            null_mut(),
            (&mut percent as *mut u32).cast(),
            &mut bytes,
        )
    };
    if result == ERROR_SUCCESS && bytes == 4 && (100..=225).contains(&percent) {
        percent as f32 / 100.
    } else {
        1.
    }
}
#[cfg(feature = "dev-host")]
fn system_theme() -> i32 {
    let mut light = 1u32;
    let mut bytes = std::mem::size_of_val(&light) as u32;
    let result = unsafe {
        RegGetValueW(
            HKEY_CURRENT_USER,
            wide("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize").as_ptr(),
            wide("AppsUseLightTheme").as_ptr(),
            RRF_RT_REG_DWORD,
            null_mut(),
            (&mut light as *mut u32).cast(),
            &mut bytes,
        )
    };
    if result == ERROR_SUCCESS && bytes == 4 && light == 0 {
        2
    } else {
        1
    }
}
pub(crate) fn wake_ui() {
    let host = UI_HOST.load(std::sync::atomic::Ordering::Acquire);
    if host != 0 {
        unsafe {
            PostMessageW(host as HWND, WM_APP + 1, 0, 0);
        }
    }
}
fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(Some(0)).collect()
}
fn dpi(hwnd: HWND) -> f32 {
    unsafe { GetDpiForWindow(hwnd).max(96) as f32 / 96. }
}
struct Registry {
    host: HWND,
    renderer: winui::Renderer,
    presented: bool,
}
thread_local! { static REGISTRY: RefCell<Option<Registry>> = const { RefCell::new(None) }; }
pub(crate) fn flush(bytes: &[u8]) -> Result<()> {
    crate::bind_thread()?;
    super::validate(bytes)?;
    REGISTRY.with(|slot| {
        let mut slot = slot
            .try_borrow_mut()
            .map_err(|_| Error::Tree("reentrant WinUI presentation"))?;
        let registry = slot
            .as_mut()
            .ok_or(Error::Tree("WinUI host not initialized"))?;
        crate::ENGINE.with(|slot| {
            let mut slot = slot
                .try_borrow_mut()
                .map_err(|_| Error::Tree("reentrant engine"))?;
            let engine = slot.as_mut().ok_or(Error::Tree("missing engine"))?;
            engine.flush(bytes)?;
            engine.report_layouts()?;
            let mut rect = RECT::default();
            unsafe {
                GetClientRect(registry.host, &mut rect);
            }
            registry.renderer.sync(engine, rect.right, rect.bottom)?;
            if !registry.presented {
                unsafe {
                    ShowWindow(registry.host, SW_SHOW);
                }
                registry.presented = true;
            }
            Ok(())
        })
    })
}
#[cfg(feature = "dev-host")]
unsafe extern "system" fn host_proc(hwnd: HWND, message: u32, w: WPARAM, l: LPARAM) -> LRESULT {
    match message {
        // Regression probe executes in the HWND's activation context, not the caller's.
        message if message == WM_APP + 3 => unsafe {
            let module = GetModuleHandleW(wide("Microsoft.UI.Input.dll").as_ptr());
            (!module.is_null()
                && windows_sys::Win32::System::LibraryLoader::GetProcAddress(
                    module,
                    c"DllGetActivationFactory".as_ptr().cast(),
                )
                .is_some()) as LRESULT
        },
        WM_CLOSE => unsafe {
            // Let the runner dispose the registry before destroying its parent.
            PostQuitMessage(0);
            0
        },
        WM_DESTROY => unsafe {
            UI_HOST.store(0, std::sync::atomic::Ordering::Release);
            PostQuitMessage(0);
            0
        },
        WM_DPICHANGED => unsafe {
            let rect = &*(l as *const RECT);
            SetWindowPos(
                hwnd,
                null_mut(),
                rect.left,
                rect.top,
                rect.right - rect.left,
                rect.bottom - rect.top,
                SWP_NOZORDER | SWP_NOACTIVATE,
            );
            0
        },
        _ => unsafe { DefWindowProcW(hwnd, message, w, l) },
    }
}
#[cfg(feature = "dev-host")]
pub fn run_preview(smoke: bool) {
    unsafe {
        use crate::dev_host;
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        // Window procedures inherit the activation context present at creation.
        // Initialize the self-contained WinUI runtime before creating ANY host HWND.
        winui::initialize().expect("initialize WinUI activation context");
        let instance = GetModuleHandleW(null());
        let name = wide("DotNativeWinUIHost");
        let class = WNDCLASSW {
            lpfnWndProc: Some(host_proc),
            hInstance: instance,
            lpszClassName: name.as_ptr(),
            hCursor: LoadCursorW(null_mut(), IDC_ARROW),
            ..std::mem::zeroed()
        };
        assert_ne!(RegisterClassW(&class), 0);
        let host = CreateWindowExW(
            0,
            name.as_ptr(),
            wide("DotNative — WinUI 3").as_ptr(),
            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            960,
            720,
            null_mut(),
            null_mut(),
            instance,
            null(),
        );
        assert!(!host.is_null());
        let scale = dpi(host);
        SetWindowPos(
            host,
            null_mut(),
            0,
            0,
            ((960. * scale) as i32).min(GetSystemMetrics(SM_CXSCREEN) - 64),
            ((720. * scale) as i32).min(GetSystemMetrics(SM_CYSCREEN) - 96),
            SWP_NOMOVE | SWP_NOZORDER,
        );
        let renderer = winui::Renderer::new(host).expect("initialize WinUI 3 renderer");
        crate::initialize(Box::<winui::NativeMeasurer>::default())
            .expect("initialize WinUI measured layout");
        REGISTRY.with(|slot| {
            *slot.borrow_mut() = Some(Registry {
                host,
                renderer,
                presented: false,
            })
        });
        UI_HOST.store(host as isize, std::sync::atomic::Ordering::Release);
        super::register_plugins(host);
        if smoke {
            flush(&super::smoke_commands()).expect("WinUI create/layout");
            REGISTRY.with(|slot| slot.borrow().as_ref().unwrap().renderer.smoke(3));
            let mut msg = MSG::default();
            for _ in 0..30 {
                while PeekMessageW(&mut msg, null_mut(), 0, 0, PM_REMOVE) != 0 {
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
                std::thread::sleep(std::time::Duration::from_millis(10));
            }
            assert_eq!(crate::events::dotnative_poll_click(), 3);
            flush(&super::smoke_commands_update()).expect("WinUI update/delete");
            frame_clock::smoke(host);
            REGISTRY.with(|slot| slot.borrow_mut().take());
            DestroyWindow(host);
            println!("PASS WinUI 3 native create, measure, layout, invoke, update and delete");
            return;
        }
        let frame_clock = frame_clock::FrameClock::start(host);
        let fallback_interval = if frame_clock.is_some() { 100 } else { 16 };
        assert_ne!(
            SetTimer(host, 1, fallback_interval, None),
            0,
            "create UI tick"
        );
        let mut last_frame = std::time::Instant::now();
        let mut message: MSG = std::mem::zeroed();
        let mut previous = (0, 0, 0u32);
        let mut text_scale = system_text_scale();
        let mut theme = system_theme();
        super::require(dev_host::dotnative_app_system_theme(theme));
        let mut last_text_scale_read = std::time::Instant::now();
        loop {
            let status = GetMessageW(&mut message, null_mut(), 0, 0);
            if status == -1 {
                panic!("Win32 message pump failed");
            }
            if status == 0 {
                break;
            }
            if (message.message == WM_TIMER
                || message.message == WM_APP + 1
                || message.message == frame_clock::FRAME_MESSAGE)
                && message.hwnd == host
            {
                let emit_frame = if message.message == frame_clock::FRAME_MESSAGE {
                    if let Some(clock) = &frame_clock {
                        clock.acknowledge();
                    }
                    last_frame = std::time::Instant::now();
                    true
                } else {
                    message.message == WM_TIMER
                        && (frame_clock.is_none()
                            || last_frame.elapsed() >= std::time::Duration::from_millis(100))
                };
                if last_text_scale_read.elapsed() >= std::time::Duration::from_millis(250) {
                    text_scale = system_text_scale();
                    let next_theme = system_theme();
                    if next_theme != theme {
                        theme = next_theme;
                        super::require(dev_host::dotnative_app_system_theme(theme));
                    }
                    last_text_scale_read = std::time::Instant::now();
                }
                let mut animations: i32 = 1;
                SystemParametersInfoW(
                    SPI_GETCLIENTAREAANIMATION,
                    0,
                    (&mut animations as *mut i32).cast(),
                    0,
                );
                let mut contrast = HIGHCONTRASTW {
                    cbSize: std::mem::size_of::<HIGHCONTRASTW>() as u32,
                    ..Default::default()
                };
                SystemParametersInfoW(
                    SPI_GETHIGHCONTRAST,
                    contrast.cbSize,
                    (&mut contrast as *mut HIGHCONTRASTW).cast(),
                    0,
                );
                super::require(dev_host::dotnative_app_accessibility(
                    (animations == 0) as i32,
                    text_scale,
                    (contrast.dwFlags & HCF_HIGHCONTRASTON != 0) as i32,
                ));
                let mut rect = RECT::default();
                GetClientRect(host, &mut rect);
                let size = (rect.right, rect.bottom, GetDpiForWindow(host));
                let scale = dpi(host);
                if size.0 > 0 && size.1 > 0 && size != previous {
                    if previous == (0, 0, 0) {
                        super::require(dev_host::dotnative_app_start(
                            size.0 as f32 / scale,
                            size.1 as f32 / scale,
                        ));
                    } else {
                        super::require(dev_host::dotnative_app_resize(
                            size.0 as f32 / scale,
                            size.1 as f32 / scale,
                        ));
                    }
                    previous = size;
                }
                if previous != (0, 0, 0) {
                    super::require(dev_host::tick(emit_frame));
                }
            }
            if IsDialogMessageW(host, &message) == 0 {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
        KillTimer(host, 1);
        drop(frame_clock);
        super::require(dev_host::dotnative_app_stop());
        REGISTRY.with(|r| r.borrow_mut().take());
        DestroyWindow(host);
    }
}
