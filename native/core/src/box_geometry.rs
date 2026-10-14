//! Shared geometry for corner clipping and inside borders. Physical edge order: L,T,R,B.
use crate::{protocol::BorderSide, Node};
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Segment {
    Move(f32, f32),
    Line(f32, f32),
    Curve([f32; 6]),
    Close,
}
pub type Path = Vec<Segment>;
pub struct BoxGeometry {
    #[cfg_attr(not(target_os = "linux"), allow(dead_code))]
    pub radii: [f32; 4],
    pub borders: [BorderSide; 4],
    #[cfg_attr(any(target_os = "linux", target_os = "windows"), allow(dead_code))]
    pub outer: Path,
    pub ring: Path,
    pub center: Path,
    pub wedges: [Path; 4],
}
impl BoxGeometry {
    #[cfg_attr(target_os = "linux", allow(dead_code))]
    pub fn new(node: &Node, width: f32, height: f32) -> Self {
        Self::styles(
            node.decoration,
            node.style.radius,
            node.visual.border_width,
            node.visual.border_color,
            width,
            height,
        )
    }
    pub fn styles(
        decoration: crate::protocol::BoxDecoration,
        radius: f32,
        border_width: f32,
        border_color: [u8; 4],
        width: f32,
        height: f32,
    ) -> Self {
        let radii = normalize(decoration.corners.unwrap_or([radius; 4]), width, height);
        let mut borders = decoration.borders.map(|b| {
            b.unwrap_or(BorderSide {
                width: border_width,
                color: border_color,
                style: 0,
            })
        });
        for (a, b, limit) in [(0, 2, width), (1, 3, height)] {
            let sum = borders[a].width as f64 + borders[b].width as f64;
            if sum > limit as f64 && sum > 0. {
                let scale = limit.max(0.) as f64 / sum;
                borders[a].width = (borders[a].width as f64 * scale) as f32;
                borders[b].width = (borders[b].width as f64 * scale) as f32;
            }
        }
        let [l, t, r, b] = borders.map(|s| s.width);
        let outer = rounded([0., 0., width, height], radii.map(|v| [v, v]));
        let mut ring = outer.clone();
        ring.extend(inset(width, height, radii, [l, t, r, b]));
        let center = inset(width, height, radii, [l / 2., t / 2., r / 2., b / 2.]);
        let wedges = [
            polygon(&[[0., 0.], [l, t], [l, height - b], [0., height]]),
            polygon(&[[0., 0.], [width, 0.], [width - r, t], [l, t]]),
            polygon(&[
                [width, 0.],
                [width, height],
                [width - r, height - b],
                [width - r, t],
            ]),
            polygon(&[
                [0., height],
                [l, height - b],
                [width - r, height - b],
                [width, height],
            ]),
        ];
        Self {
            radii,
            borders,
            outer,
            ring,
            center,
            wedges,
        }
    }
}
pub fn normalize(mut radii: [f32; 4], width: f32, height: f32) -> [f32; 4] {
    let mut factor = 1f64;
    for (a, b, limit) in [(0, 1, width), (3, 2, width), (0, 3, height), (1, 2, height)] {
        let sum = radii[a] as f64 + radii[b] as f64;
        if sum > 0. {
            factor = factor.min(limit.max(0.) as f64 / sum);
        }
    }
    for r in &mut radii {
        *r = (*r as f64 * factor) as f32;
    }
    radii
}
fn inset(w: f32, h: f32, r: [f32; 4], [l, t, right, b]: [f32; 4]) -> Path {
    rounded(
        [l, t, (w - l - right).max(0.), (h - t - b).max(0.)],
        [
            [r[0] - l, r[0] - t],
            [r[1] - right, r[1] - t],
            [r[2] - right, r[2] - b],
            [r[3] - l, r[3] - b],
        ],
    )
}
pub fn polygon(points: &[[f32; 2]]) -> Path {
    let mut path = vec![];
    for (i, p) in points.iter().enumerate() {
        path.push(if i == 0 {
            Segment::Move(p[0], p[1])
        } else {
            Segment::Line(p[0], p[1])
        });
    }
    path.push(Segment::Close);
    path
}
fn rounded([x, y, w, h]: [f32; 4], mut r: [[f32; 2]; 4]) -> Path {
    if w <= 0. || h <= 0. {
        return vec![];
    }
    for pair in &mut r {
        for v in pair {
            *v = v.max(0.);
        }
    }
    let mut factor = 1f64;
    for (a, b, axis, limit) in [(0, 1, 0, w), (3, 2, 0, w), (0, 3, 1, h), (1, 2, 1, h)] {
        let sum = r[a][axis] as f64 + r[b][axis] as f64;
        if sum > 0. {
            factor = factor.min(limit as f64 / sum);
        }
    }
    for pair in &mut r {
        for v in pair {
            *v = (*v as f64 * factor) as f32;
        }
    }
    let [[a, aa], [b, bb], [c, cc], [d, dd]] = r;
    let k = 0.5522848;
    vec![
        Segment::Move(x + a, y),
        Segment::Line(x + w - b, y),
        Segment::Curve([x + w - b + k * b, y, x + w, y + bb - k * bb, x + w, y + bb]),
        Segment::Line(x + w, y + h - cc),
        Segment::Curve([
            x + w,
            y + h - cc + k * cc,
            x + w - c + k * c,
            y + h,
            x + w - c,
            y + h,
        ]),
        Segment::Line(x + d, y + h),
        Segment::Curve([x + d - k * d, y + h, x, y + h - dd + k * dd, x, y + h - dd]),
        Segment::Line(x, y + aa),
        Segment::Curve([x, y + aa - k * aa, x + a - k * a, y, x + a, y]),
        Segment::Close,
    ]
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn oversized_corners_and_edges_remain_finite_and_fit() {
        assert_eq!(
            normalize([80., 80., 0., 0.], 100., 100.),
            [50., 50., 0., 0.]
        );
        assert_eq!(normalize([f32::MAX; 4], 100., 40.), [20.; 4]);
        let g = BoxGeometry::styles(Default::default(), f32::MAX, f32::MAX, [255; 4], 100., 40.);
        assert_eq!(g.borders.map(|b| b.width), [50., 20., 50., 20.]);
        for segment in g.ring.iter().chain(g.center.iter()) {
            match segment {
                Segment::Move(x, y) | Segment::Line(x, y) => {
                    assert!(x.is_finite() && y.is_finite())
                }
                Segment::Curve(v) => assert!(v.iter().all(|x| x.is_finite())),
                Segment::Close => (),
            }
        }
        assert!(
            BoxGeometry::styles(Default::default(), 4., 2., [255; 4], 0., 0.)
                .outer
                .is_empty()
        );
    }
}
