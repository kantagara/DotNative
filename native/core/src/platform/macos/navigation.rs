use super::*;
use block2::RcBlock;
use objc2_app_kit::NSWorkspace;
use objc2_foundation::NSNumber;
use objc2_quartz_core::{CABasicAnimation, CAMediaTiming, CAMediaTimingFunction, CATransaction};

#[derive(Default)]
pub(super) struct State {
    page: u32,
    outgoing: u32,
    depth: usize,
    busy: Rc<Cell<bool>>,
    cancelled: Rc<Cell<bool>>,
    layers: Vec<Retained<objc2_quartz_core::CALayer>>,
}

impl Drop for State {
    fn drop(&mut self) {
        self.cancelled.set(true);
        for layer in &self.layers {
            layer.removeAnimationForKey(&NSString::from_str("dotnative.navigation"));
        }
    }
}

pub(super) fn active_descendant(engine: &Engine, wanted: u32) -> bool {
    fn contains(engine: &Engine, id: u32, wanted: u32) -> bool {
        if id == wanted {
            return true;
        }
        let Some(node) = engine.node(id) else {
            return false;
        };
        if node.kind == Kind::Navigation {
            return node
                .children
                .get((node.control.value as usize).saturating_sub(1))
                .is_some_and(|id| contains(engine, *id, wanted));
        }
        node.children.iter().any(|id| contains(engine, *id, wanted))
    }
    contains(engine, engine.root(), wanted)
}

impl Registry {
    pub(super) fn sync_navigation(&mut self, engine: &Engine) {
        self.navigation.retain(|id, _| engine.node(*id).is_some());
        for (id, node) in engine.nodes().filter(|(_, n)| n.kind == Kind::Navigation) {
            let state = self.navigation.entry(id).or_default();
            let depth = node.control.value as usize;
            let target = node.children[depth - 1];
            if state.busy.get() {
                continue;
            }
            for page in &node.children {
                self.entries[page].view.setHidden(*page != state.page);
            }
            state.layers.clear();
            if state.page == target {
                continue;
            }
            let from = self.entries.get(&state.page).map(|e| e.view.clone());
            let to = self.entries[&target].view.clone();
            let forward = depth > state.depth;
            state.outgoing = state.page;
            state.page = target;
            state.depth = depth;
            to.setHidden(false);
            let reduced: bool = unsafe {
                msg_send![
                    &NSWorkspace::sharedWorkspace(),
                    accessibilityDisplayShouldReduceMotion
                ]
            };
            if from.is_none() || node.navigation_transition == 3 || reduced {
                if let Some(from) = from {
                    from.setHidden(true);
                }
                crate::events::emit(id, 5, depth as f32, String::new());
                continue;
            }
            let from = from.unwrap();
            let container = &self.entries[&id].view;
            if let Some(layer) = container.layer() {
                layer.setMasksToBounds(true);
            }
            if let Some(window) = container.window() {
                window.makeFirstResponder(None);
            }
            let gate = container
                .downcast_ref::<FlippedView>()
                .unwrap()
                .ivars()
                .hit_test
                .clone();
            gate.set(true);
            state.busy.set(true);
            crate::events::emit(id, 6, 0., String::new());
            let fade = node.navigation_transition == 2;
            unsafe {
                let _: () = msg_send![container, addSubview: if forward || fade { &**to } else { &**from }, positioned: 1isize, relativeTo: std::ptr::null::<NSView>()];
            }
            let width = container.bounds().size.width;
            let (y1, y2) = match node.navigation_easing {
                0 => (1. / 3., 2. / 3.),
                2 => (1., 1.),
                3 => (0., 0.),
                _ => (0., 1.),
            };
            let timing = CAMediaTimingFunction::functionWithControlPoints(1. / 3., y1, 2. / 3., y2);
            let seconds = node.navigation_duration as f64;
            let from_layer = from.layer().expect("layer-backed page");
            let to_layer = to.layer().expect("layer-backed page");
            let animate = |layer: &objc2_quartz_core::CALayer, key: &str, start: f64, end: f64| {
                let animation =
                    CABasicAnimation::animationWithKeyPath(Some(&NSString::from_str(key)));
                animation.setDuration(seconds);
                animation.setTimingFunction(Some(&timing));
                animation.setRemovedOnCompletion(false);
                animation.setFillMode(&NSString::from_str("both"));
                unsafe {
                    animation.setFromValue(Some(&NSNumber::new_f64(start)));
                    animation.setToValue(Some(&NSNumber::new_f64(end)));
                }
                layer.addAnimation_forKey(
                    &animation,
                    Some(&NSString::from_str("dotnative.navigation")),
                );
            };
            state.layers = vec![from_layer.clone(), to_layer.clone()];
            let busy = state.busy.clone();
            let cancelled = state.cancelled.clone();
            CATransaction::begin();
            CATransaction::setDisableActions(true);
            let finished_from = from_layer.clone();
            let finished_to = to_layer.clone();
            unsafe {
                CATransaction::setCompletionBlock(Some(&RcBlock::new(move || {
                    let _transaction = NoAnimations::new();
                    from.setHidden(true);
                    // Keep the final presentation until the outgoing page is hidden.
                    finished_from
                        .removeAnimationForKey(&NSString::from_str("dotnative.navigation"));
                    finished_to.removeAnimationForKey(&NSString::from_str("dotnative.navigation"));
                    busy.set(false);
                    gate.set(false);
                    if !cancelled.get() {
                        crate::events::emit(id, 5, depth as f32, String::new());
                    }
                })));
            }
            if fade {
                animate(&to_layer, "opacity", 0., 1.);
            } else {
                animate(
                    &from_layer,
                    "transform.translation.x",
                    0.,
                    if forward { -0.25 * width } else { width },
                );
                animate(
                    &to_layer,
                    "transform.translation.x",
                    if forward { width } else { -0.25 * width },
                    0.,
                );
            }
            CATransaction::commit();
        }
    }
}
