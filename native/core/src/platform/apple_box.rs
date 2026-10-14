use crate::{
    box_geometry::{BoxGeometry, Path, Segment},
    Kind, Node,
};

/// Gradient, box, background and transform layers follow the view bounds.
/// After setFrame we reset CATransform3D, so a geometry change must re-apply
/// visual even when opacity/shadow/clip records are unchanged.
pub(super) fn layer_needs_sync(old: Option<&Node>, node: &Node, geometry_changed: bool) -> bool {
    geometry_changed
        || old.is_none_or(|old| {
            old.kind != node.kind
                || old.enabled != node.enabled
                || old.behavior.visibility != node.behavior.visibility
                || old.visual != node.visual
                || old.projection != node.projection
                || old.gradient != node.gradient
                || old.background_image != node.background_image
                || old.background_fit != node.background_fit
                || old.decoration != node.decoration
                || old.image_fit != node.image_fit
                || old.style != node.style
        })
}
use objc2::rc::Retained;
use objc2_core_foundation::CFRetained;
use objc2_core_graphics::{CGColor, CGMutablePath};
use objc2_foundation::{NSArray, NSNumber, NSString};
use objc2_quartz_core::{
    kCAFillRuleEvenOdd, kCALineCapButt, kCALineCapRound, CALayer, CAShapeLayer,
};

fn path(commands: &Path) -> CFRetained<CGMutablePath> {
    let path = CGMutablePath::new();
    for command in commands {
        unsafe {
            match *command {
                Segment::Move(x, y) => {
                    CGMutablePath::move_to_point(Some(&path), std::ptr::null(), x as _, y as _)
                }
                Segment::Line(x, y) => {
                    CGMutablePath::add_line_to_point(Some(&path), std::ptr::null(), x as _, y as _)
                }
                Segment::Curve([a, b, c, d, e, f]) => CGMutablePath::add_curve_to_point(
                    Some(&path),
                    std::ptr::null(),
                    a as _,
                    b as _,
                    c as _,
                    d as _,
                    e as _,
                    f as _,
                ),
                Segment::Close => CGMutablePath::close_subpath(Some(&path)),
            }
        }
    }
    path
}
fn color(c: [u8; 4]) -> CFRetained<CGColor> {
    CGColor::new_generic_rgb(
        c[0] as f64 / 255.,
        c[1] as f64 / 255.,
        c[2] as f64 / 255.,
        c[3] as f64 / 255.,
    )
}
fn named(parent: &CALayer, name: &str) -> Option<Retained<CALayer>> {
    unsafe { parent.sublayers() }.and_then(|layers| {
        layers
            .iter()
            .find(|l| l.name().is_some_and(|n| n.to_string() == name))
    })
}
fn remove(parent: &CALayer, name: &str) {
    if let Some(layer) = named(parent, name) {
        layer.removeFromSuperlayer();
    }
}
fn mask(commands: &Path) -> Retained<CAShapeLayer> {
    let layer = CAShapeLayer::new();
    layer.setPath(Some(&path(commands)));
    layer.setFillColor(Some(&color([0, 0, 0, 255])));
    layer
}
pub(super) fn clip(layer: &CALayer, node: &Node, enabled: bool) {
    if node.decoration.corners.is_some() {
        layer.setCornerRadius(0.);
        let bounds = layer.bounds();
        let g = BoxGeometry::new(node, bounds.size.width as f32, bounds.size.height as f32);
        let shape = mask(&g.outer);
        shape.setFrame(bounds);
        unsafe {
            layer.setMask(if enabled { Some(&shape) } else { None });
        }
    } else {
        unsafe {
            layer.setMask(None);
        }
        layer.setCornerRadius(node.style.radius as _);
    }
}
pub(super) fn apply(parent: &CALayer, node: &Node) {
    let custom = node.decoration != Default::default();
    if !custom {
        remove(parent, "DotNativeBorders");
        if named(parent, "DotNativeBoxFill").is_some() {
            parent.setBackgroundColor(Some(&color(node.style.background)));
            remove(parent, "DotNativeBoxFill");
        }
        clip(parent, node, node.visual.clip);
        parent.setShadowPath(None);
        order_backgrounds(parent);
        return;
    }
    let bounds = parent.bounds();
    let g = BoxGeometry::new(node, bounds.size.width as f32, bounds.size.height as f32);
    clip(
        parent,
        node,
        node.visual.clip && node.kind != Kind::NavigationPage,
    );
    parent.setShadowPath(Some(&path(&g.outer)));
    parent.setBorderWidth(0.);
    remove(parent, "DotNativeBoxFill");
    if node.decoration.corners.is_some() && !node.visual.clip {
        parent.setBackgroundColor(None);
        let fill = CAShapeLayer::new();
        fill.setName(Some(&NSString::from_str("DotNativeBoxFill")));
        fill.setPath(Some(&path(&g.outer)));
        fill.setFillColor(Some(&color(node.style.background)));
        parent.insertSublayer_atIndex(&fill, 0);
    } else {
        parent.setBackgroundColor(Some(&color(node.style.background)));
    }
    order_backgrounds(parent);
    remove(parent, "DotNativeBorders");
    if g.borders.iter().all(|b| b.width == 0. || b.color[3] == 0) {
        return;
    }
    let border = CALayer::new();
    border.setName(Some(&NSString::from_str("DotNativeBorders")));
    border.setFrame(bounds);
    let ring_mask = mask(&g.ring);
    unsafe {
        ring_mask.setFillRule(kCAFillRuleEvenOdd);
        border.setMask(Some(&ring_mask));
    }
    for (i, side) in g.borders.iter().enumerate() {
        if side.width == 0. || side.color[3] == 0 {
            continue;
        }
        let edge = CAShapeLayer::new();
        edge.setFrame(objc2_core_foundation::CGRect::new(
            objc2_core_foundation::CGPoint::new(0., 0.),
            bounds.size,
        ));
        if side.style == 0 {
            edge.setPath(Some(&path(&g.ring)));
            unsafe {
                edge.setFillRule(kCAFillRuleEvenOdd);
            }
            edge.setFillColor(Some(&color(side.color)));
        } else {
            edge.setPath(Some(&path(&g.center)));
            edge.setFillColor(None);
            edge.setStrokeColor(Some(&color(side.color)));
            edge.setLineWidth(side.width as _);
            let w = side.width;
            let dash = [
                NSNumber::new_f32(if side.style == 1 { 3. * w } else { 0.001 * w }),
                NSNumber::new_f32(2. * w),
            ];
            edge.setLineDashPattern(Some(&NSArray::from_retained_slice(&dash)));
            unsafe {
                edge.setLineCap(if side.style == 2 {
                    kCALineCapRound
                } else {
                    kCALineCapButt
                });
            }
        }
        unsafe {
            edge.setMask(Some(&mask(&g.wedges[i])));
        }
        border.addSublayer(&edge);
    }
    parent.addSublayer(&border);
}

fn order_backgrounds(parent: &CALayer) {
    let mut index = 0;
    for name in [
        "DotNativeBoxFill",
        "DotNativeGradient",
        "DotNativeBackgroundImage",
    ] {
        if let Some(layer) = named(parent, name) {
            parent.insertSublayer_atIndex(&layer, index);
            index += 1;
        }
    }
}
