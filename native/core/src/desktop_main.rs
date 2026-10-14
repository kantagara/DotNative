//! CLI-owned native runner. A bundled preview may put a self-contained managed
//! host beside it, allowing the sample to open without an SDK or terminal.
#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

#[cfg(target_os = "windows")]
struct BundledHost(std::process::Child);
#[cfg(target_os = "windows")]
impl Drop for BundledHost {
    fn drop(&mut self) {
        // --once lets the managed host dispose DI scopes after socket shutdown.
        for _ in 0..60 {
            if self.0.try_wait().ok().flatten().is_some() {
                return;
            }
            std::thread::sleep(std::time::Duration::from_millis(50));
        }
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}
fn main() {
    let smoke = std::env::args().any(|arg| arg == "--smoke");
    #[cfg(target_os = "windows")]
    let _host = {
        use std::os::windows::process::CommandExt;
        let executable = std::env::current_exe().expect("resolve preview directory");
        let host = executable
            .parent()
            .unwrap()
            .join("managed/DotNative.DevHost.exe");
        if !smoke && host.is_file() {
            Some(BundledHost(
                std::process::Command::new(host)
                    .args(["WINDOWS", "--once"])
                    .creation_flags(0x08000000) // CREATE_NO_WINDOW for the managed host
                    .spawn()
                    .expect("start bundled managed preview host"),
            ))
        } else {
            None
        }
    };
    #[cfg(any(target_os = "linux", target_os = "windows"))]
    core::platform::desktop::run_preview(smoke);
    #[cfg(not(any(target_os = "linux", target_os = "windows")))]
    {
        let _ = smoke;
        eprintln!("Build and run this desktop preview on Windows or Linux.");
        std::process::exit(2);
    }
}
