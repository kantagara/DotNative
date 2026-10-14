use crate::Node;
use objc2::{rc::Retained, runtime::AnyObject};
use objc2_foundation::{NSDictionary, NSNumber, NSString};
// Core Text descriptor keys are toll-free bridged CFString/NSString objects.
#[link(name = "CoreText", kind = "framework")]
extern "C" {
    static kCTFontVariationAttribute: &'static NSString;
}
pub(super) fn variation_attributes(node: &Node) -> Retained<NSDictionary<NSString, AnyObject>> {
    let keys: Vec<_> = node
        .font_axes
        .iter()
        .map(|(tag, _)| NSNumber::new_u32(u32::from_be_bytes(*tag)))
        .collect();
    let values: Vec<_> = node
        .font_axes
        .iter()
        .map(|(_, value)| NSNumber::new_f32(*value))
        .collect();
    let variations = NSDictionary::<NSNumber, NSNumber>::from_slices(
        &keys.iter().map(|v| &**v).collect::<Vec<_>>(),
        &values.iter().map(|v| &**v).collect::<Vec<_>>(),
    );
    unsafe {
        NSDictionary::from_slices(&[kCTFontVariationAttribute], &[&*variations as &AnyObject])
    }
}

#[link(name = "CoreText", kind = "framework")]
extern "C" {
    fn CTFontManagerRegisterGraphicsFont(
        font: *const objc2_core_graphics::CGFont,
        error: *mut *const std::ffi::c_void,
    ) -> bool;
    fn CTFontManagerUnregisterGraphicsFont(
        font: *const objc2_core_graphics::CGFont,
        error: *mut *const std::ffi::c_void,
    ) -> bool;
}
struct RegisteredFont(objc2_core_foundation::CFRetained<objc2_core_graphics::CGFont>);
impl Drop for RegisteredFont {
    fn drop(&mut self) {
        unsafe {
            CTFontManagerUnregisterGraphicsFont(&*self.0, std::ptr::null_mut());
        }
    }
}
pub(super) fn register(node: &Node) -> crate::Result<()> {
    crate::font_assets::ensure(node, |asset| {
        let data = objc2_core_foundation::CFData::from_bytes(&asset.data);
        let provider = objc2_core_graphics::CGDataProvider::with_cf_data(Some(&data))
            .ok_or(crate::Error::Measurement("font provider failed"))?;
        let font = objc2_core_graphics::CGFont::with_data_provider(&provider)
            .ok_or(crate::Error::Measurement("invalid font data"))?;
        if !unsafe { CTFontManagerRegisterGraphicsFont(&*font, std::ptr::null_mut()) } {
            return Err(crate::Error::Measurement(
                "font registration failed (possibly already installed)",
            ));
        }
        Ok(Box::new(RegisteredFont(font)))
    })
}
