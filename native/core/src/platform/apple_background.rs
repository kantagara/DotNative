use crate::Node;
use objc2::rc::Retained;
use objc2_core_foundation::CGSize;
use objc2_quartz_core::{
    kCAGravityCenter, kCAGravityResize, kCAGravityResizeAspect, kCAGravityResizeAspectFill, CALayer,
};

pub(super) struct Background {
    layer: Retained<CALayer>,
    size: CGSize,
}
impl Drop for Background {
    fn drop(&mut self) {
        self.layer.removeFromSuperlayer();
    }
}

pub(super) fn sync(parent: &CALayer, node: &Node, entry: &mut Option<Background>, changed: bool) {
    if node.background_image.is_empty() {
        *entry = None;
        return;
    }
    if changed || entry.is_none() {
        let asset = node.background_asset();
        #[cfg(target_os = "ios")]
        let image = super::ios::decoded_image(&asset).expect("preflighted background image");
        #[cfg(target_os = "macos")]
        let image = super::macos::decoded_image(&asset).expect("preflighted background image");
        #[cfg(target_os = "ios")]
        let (cg, size) = unsafe { (image.CGImage(), image.size()) };
        #[cfg(target_os = "macos")]
        let (cg, size) = unsafe {
            (
                image.CGImageForProposedRect_context_hints(std::ptr::null_mut(), None, None),
                image.size(),
            )
        };
        let layer = CALayer::new();
        layer.setName(Some(&objc2_foundation::NSString::from_str(
            "DotNativeBackgroundImage",
        )));
        {
            let cg = cg.expect("preflighted raster background image");
            layer.setContentsScale(
                objc2_core_graphics::CGImage::width(Some(&cg)) as f64 / size.width.max(1.),
            );
            // Core Animation accepts a CGImage as its retained contents object.
            unsafe {
                layer.setContents(Some(
                    &*((&*cg as *const _) as *const objc2::runtime::AnyObject),
                ));
            }
        }
        *entry = Some(Background { layer, size });
    }
    let background = entry.as_ref().unwrap();
    let bounds = parent.bounds();
    background.layer.setFrame(bounds);
    background.layer.setCornerRadius(node.style.radius as _);
    background.layer.setMasksToBounds(true);
    super::apple_box::clip(&background.layer, node, true);
    let fits =
        background.size.width <= bounds.size.width && background.size.height <= bounds.size.height;
    unsafe {
        background
            .layer
            .setContentsGravity(match node.background_fit {
                1 => kCAGravityResizeAspectFill,
                2 => kCAGravityResize,
                3 => kCAGravityCenter,
                4 if fits => kCAGravityCenter,
                _ => kCAGravityResizeAspect,
            });
    }
    parent.insertSublayer_atIndex(
        &background.layer,
        if node.gradient.is_some() { 1 } else { 0 },
    );
}
