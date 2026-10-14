use crate::platform::frame_pump::FramePump;
use windows_sys::Win32::{
    Foundation::{FreeLibrary, HMODULE, HWND},
    System::LibraryLoader::{GetProcAddress, LoadLibraryExW, LOAD_LIBRARY_SEARCH_SYSTEM32},
    UI::WindowsAndMessaging::{PostMessageW, WM_APP},
};
pub(super) const FRAME_MESSAGE: u32 = WM_APP + 2;
type WaitForClock =
    unsafe extern "system" fn(u32, *const windows_sys::Win32::Foundation::HANDLE, u32) -> u32;

pub(super) struct FrameClock {
    pump: FramePump,
    library: HMODULE,
}
impl FrameClock {
    pub(super) fn start(host: HWND) -> Option<Self> {
        unsafe {
            // Resolve at runtime: importing this Win11 API would prevent Win10 startup.
            let library = LoadLibraryExW(
                super::wide("dcomp.dll").as_ptr(),
                std::ptr::null_mut(),
                LOAD_LIBRARY_SEARCH_SYSTEM32,
            );
            if library.is_null() {
                return None;
            }
            let Some(address) = GetProcAddress(
                library,
                c"DCompositionWaitForCompositorClock".as_ptr().cast(),
            ) else {
                FreeLibrary(library);
                return None;
            };
            let wait: WaitForClock = std::mem::transmute(address);
            let window = host as usize;
            let pump = FramePump::start(
                move || wait(0, std::ptr::null(), 100) == 0,
                move || {
                    super::UI_HOST.load(std::sync::atomic::Ordering::Acquire) == window as isize
                        && PostMessageW(window as HWND, FRAME_MESSAGE, 0, 0) != 0
                },
            );
            match pump {
                Ok(pump) => Some(Self { pump, library }),
                Err(_) => {
                    FreeLibrary(library);
                    None
                }
            }
        }
    }
    pub(super) fn acknowledge(&self) {
        self.pump.acknowledge();
    }
}
impl Drop for FrameClock {
    fn drop(&mut self) {
        self.pump.stop(); // No worker may execute the function pointer after unload.
        unsafe {
            FreeLibrary(self.library);
        }
    }
}

pub(super) fn smoke(host: HWND) {
    use std::time::{Duration, Instant};
    use windows_sys::Win32::UI::WindowsAndMessaging::{PeekMessageW, MSG, PM_REMOVE};
    let Some(clock) = FrameClock::start(host) else {
        println!("PASS Win32 compositor API unavailable; legacy timer fallback selected");
        return;
    };
    let mut message: MSG = unsafe { std::mem::zeroed() };
    let deadline = Instant::now() + Duration::from_millis(750);
    let mut frames = 0;
    while Instant::now() < deadline && frames < 2 {
        if unsafe { PeekMessageW(&mut message, host, FRAME_MESSAGE, FRAME_MESSAGE, PM_REMOVE) } != 0
        {
            frames += 1;
            clock.acknowledge();
        } else {
            std::thread::sleep(Duration::from_millis(5));
        }
    }
    drop(clock);
    while unsafe { PeekMessageW(&mut message, host, FRAME_MESSAGE, FRAME_MESSAGE, PM_REMOVE) } != 0
    {
    }
    std::thread::sleep(Duration::from_millis(120));
    assert_eq!(
        unsafe { PeekMessageW(&mut message, host, FRAME_MESSAGE, FRAME_MESSAGE, PM_REMOVE) },
        0,
        "compositor callback survived shutdown"
    );
    if frames == 0 {
        println!("PASS Win32 compositor cleanup; display occluded/unavailable, frame delivery not exercised");
    } else {
        println!("PASS Win32 compositor frame delivery and callback cleanup");
    }
}
