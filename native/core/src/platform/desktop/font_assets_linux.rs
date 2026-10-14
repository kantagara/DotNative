use crate::{font_assets, Error, Node, Result};
use std::{
    ffi::{c_void, CString},
    io::Write,
    os::unix::{ffi::OsStrExt, fs::OpenOptionsExt},
    sync::atomic::{AtomicU64, Ordering},
};
#[link(name = "fontconfig")]
extern "C" {
    fn FcConfigGetCurrent() -> *mut c_void;
    fn FcConfigAppFontAddFile(config: *mut c_void, file: *const u8) -> i32;
}
#[link(name = "pangocairo-1.0")]
extern "C" {
    fn pango_cairo_font_map_get_default() -> *mut c_void;
}
#[link(name = "pangoft2-1.0")]
extern "C" {
    fn pango_fc_font_map_config_changed(map: *mut c_void);
}
struct FontFile(std::path::PathBuf);
impl Drop for FontFile {
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.0);
    }
}
pub(super) fn register(node: &Node) -> Result<()> {
    static NEXT: AtomicU64 = AtomicU64::new(1);
    font_assets::ensure(node, |asset| {
        let mut created = None;
        for _ in 0..1024 {
            let path = std::env::temp_dir().join(format!(
                "dotnative-font-{}-{}.otf",
                std::process::id(),
                NEXT.fetch_add(1, Ordering::Relaxed)
            ));
            match std::fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .mode(0o600)
                .open(&path)
            {
                Ok(file) => {
                    created = Some((FontFile(path), file));
                    break;
                }
                Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => continue,
                Err(_) => return Err(Error::Measurement("cannot create private font cache")),
            }
        }
        let (path, mut file) = created.ok_or(Error::Measurement("font cache paths exhausted"))?;
        file.write_all(&asset.data)
            .map_err(|_| Error::Measurement("cannot write font cache"))?;
        drop(file);
        let name = CString::new(path.0.as_os_str().as_bytes())
            .map_err(|_| Error::Measurement("invalid font cache path"))?;
        if unsafe { FcConfigAppFontAddFile(FcConfigGetCurrent(), name.as_ptr().cast()) } == 0 {
            return Err(Error::Measurement("fontconfig rejected font"));
        }
        unsafe {
            pango_fc_font_map_config_changed(pango_cairo_font_map_get_default());
        }
        Ok(Box::new(path))
    })
}
