//! Shared premultiplied sRGB interpolation for raster fallbacks and wire tests.
use crate::protocol::Gradient;
impl Gradient {
    #[cfg_attr(not(test), allow(dead_code))]
    pub(crate) fn sample(&self, x: f32, y: f32, width: f32, height: f32) -> [u8; 4] {
        let dx = x - self.start[0] * width;
        let dy = y - self.start[1] * height;
        let t = match self.kind {
            2 => dx.hypot(dy) / (self.end[0] * width.min(height)).max(f32::MIN_POSITIVE),
            3 => dx.atan2(-dy).rem_euclid(std::f32::consts::TAU) / std::f32::consts::TAU,
            _ => {
                let vx = (self.end[0] - self.start[0]) * width;
                let vy = (self.end[1] - self.start[1]) * height;
                (dx * vx + dy * vy) / (vx * vx + vy * vy).max(f32::MIN_POSITIVE)
            }
        }
        .clamp(0., 1.);
        let right = self
            .stops
            .partition_point(|s| s.0 < t)
            .clamp(1, self.stops.len() - 1);
        let (lo, a) = self.stops[right - 1];
        let (hi, b) = self.stops[right];
        let f = (t - lo) / (hi - lo);
        let alpha = a[3] as f32 * (1. - f) + b[3] as f32 * f;
        let mut out = [0; 4];
        out[3] = alpha.round() as u8;
        if alpha > 0. {
            for c in 0..3 {
                out[c] = ((a[c] as f32 * a[3] as f32 * (1. - f) + b[c] as f32 * b[3] as f32 * f)
                    / alpha)
                    .round() as u8;
            }
        }
        out
    }
}
