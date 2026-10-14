//! Centered object-fit geometry; keeps intrinsic measurement separate from painting.
#[cfg_attr(
    any(target_os = "android", target_os = "ios", target_os = "windows"),
    allow(dead_code)
)]
pub(crate) fn rect(fit: u8, source: [f32; 2], bounds: [f32; 2]) -> [f32; 4] {
    let [sw, sh] = source;
    let [bw, bh] = bounds;
    if source
        .iter()
        .chain(bounds.iter())
        .any(|v| !v.is_finite() || *v <= 0.)
    {
        return [0.; 4];
    }
    if fit == 2 {
        return [0., 0., bw, bh];
    }
    let contain = (bw / sw).min(bh / sh);
    let scale = match fit {
        1 => (bw / sw).max(bh / sh),
        3 => 1.,
        4 => contain.min(1.),
        _ => contain,
    };
    let w = sw * scale;
    let h = sh * scale;
    [(bw - w) / 2., (bh - h) / 2., w, h]
}
