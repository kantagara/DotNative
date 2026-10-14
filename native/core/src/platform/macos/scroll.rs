use super::*;
use objc2_app_kit::NSClipView;
use objc2_app_kit::{NSClickGestureRecognizer, NSGestureRecognizerState, NSPanGestureRecognizer};

pub(super) struct ScrollIvars {
    id: u32,
    drag_origin: Cell<f64>,
    drag_velocity: Cell<Option<(std::time::Instant, f64)>>,
    drag_sample: Cell<Option<(std::time::Instant, f64)>>,
    revision: Cell<u32>,
    observed: Cell<bool>,
    horizontal: Cell<bool>,
    updating: Cell<bool>,
    last: Cell<Option<(u32, f32, f32)>>,
}
define_class!(
    #[unsafe(super(NSScrollView))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeMacScrollView"]
    #[ivars=ScrollIvars]
    pub(super) struct NativeScroll;
    impl NativeScroll {
        #[unsafe(method(dragged:))]
        fn dragged(&self, gesture: &NSPanGestureRecognizer) {
            let clip = self.contentView();
            let Some(document) = self.documentView() else { return; };
            let state = self.ivars();
            let horizontal = state.horizontal.get();
            let bounds = clip.bounds();
            if gesture.state() == NSGestureRecognizerState::Began {
                state.drag_velocity.set(None);
                state.drag_sample.set(None);
                state.drag_origin.set(if horizontal { bounds.origin.x } else { bounds.origin.y });
                crate::events::emit(state.id, 13, 1., String::new());
            }
            if matches!(gesture.state(), NSGestureRecognizerState::Began | NSGestureRecognizerState::Changed) {
                let velocity = gesture.velocityInView(Some(self));
                let speed = if horizontal { -velocity.x } else if self.isFlipped() { -velocity.y } else { velocity.y };
                let delta = gesture.translationInView(Some(self));
                let extent = document.frame().size;
                let distance = if horizontal { delta.x } else if self.isFlipped() { delta.y } else { -delta.y };
                let now = std::time::Instant::now();
                let previous = state.drag_sample.replace(Some((now, distance)));
                let estimated = previous.and_then(|(time, position)| {
                    let elapsed = now.duration_since(time).as_secs_f64();
                    (elapsed > 0.001 && elapsed < 0.15).then_some(-(distance-position)/elapsed)
                }).unwrap_or(0.);
                let speed = if speed.abs() >= 30. { speed } else { estimated };
                if speed.abs() >= 10. {
                    state.drag_velocity.set(Some((now, speed)));
                }
                let maximum = if horizontal { extent.width-bounds.size.width } else { extent.height-bounds.size.height };
                let offset = (state.drag_origin.get()-distance).clamp(0., maximum.max(0.));
                document.scrollPoint(if horizontal { CGPoint::new(offset,0.) } else { CGPoint::new(0.,offset) });
                self.report();
            }
            if matches!(gesture.state(), NSGestureRecognizerState::Ended | NSGestureRecognizerState::Cancelled | NSGestureRecognizerState::Failed) {
                crate::events::emit(state.id, 13, 0., String::new());
                if gesture.state() == NSGestureRecognizerState::Ended {
                    let velocity = gesture.velocityInView(Some(self));
                    let speed = if horizontal { -velocity.x } else if self.isFlipped() { -velocity.y } else { velocity.y };
                    let speed = if speed.abs() >= 30. { speed } else {
                        state.drag_velocity.get().filter(|(time, _)| time.elapsed().as_millis() < 150).map_or(0., |(time, velocity)| velocity * (-8. * time.elapsed().as_secs_f64()).exp())
                    };
                    crate::events::emit(state.id, 14, speed as f32, String::new());
                }
            }
        }
        #[unsafe(method(tapped:))]
        fn tapped(&self, gesture: &NSClickGestureRecognizer) {
            let point = gesture.locationInView(Some(self));
            let y = if self.isFlipped() {point.y} else {self.bounds().size.height-point.y};
            crate::events::emit(self.ivars().id,12,y as f32,String::new());
        }
        #[unsafe(method(reflectScrolledClipView:))]
        fn reflected(&self, clip: &NSClipView) {
            unsafe { let _: () = msg_send![super(self), reflectScrolledClipView: clip]; }
            self.report();
        }
    }
);
impl NativeScroll {
    pub(super) fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(ScrollIvars {
            id,
            drag_origin: Cell::new(0.),
            drag_velocity: Cell::new(None),
            drag_sample: Cell::new(None),
            revision: Cell::new(0),
            observed: Cell::new(false),
            horizontal: Cell::new(false),
            updating: Cell::new(true),
            last: Cell::new(None),
        });
        let scroll: Retained<Self> =
            unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] };
        let tap: Retained<NSClickGestureRecognizer> = unsafe {
            msg_send![NSClickGestureRecognizer::alloc(mtm), initWithTarget: &*scroll, action: sel!(tapped:)]
        };
        scroll.addGestureRecognizer(&tap);
        let pan: Retained<NSPanGestureRecognizer> = unsafe {
            msg_send![NSPanGestureRecognizer::alloc(mtm), initWithTarget: &*scroll, action: sel!(dragged:)]
        };
        scroll.addGestureRecognizer(&pan);
        scroll
    }
    pub(super) fn begin_update(&self) {
        self.ivars().updating.set(true);
    }
    pub(super) fn configure(&self, node: &Node, new: bool) {
        let state = self.ivars();
        state.horizontal.set(node.scroll_axis == 1);
        state.observed.set(node.scroll_position.2);
        if new || state.revision.get() != node.scroll_position.0 {
            state.revision.set(node.scroll_position.0);
            if let Some(document) = self.documentView() {
                let viewport = self.contentView().bounds().size;
                let extent = document.frame().size;
                let offset = node.scroll_position.1 as f64;
                document.scrollPoint(if state.horizontal.get() {
                    CGPoint::new(offset.min((extent.width - viewport.width).max(0.)), 0.)
                } else {
                    CGPoint::new(0., offset.min((extent.height - viewport.height).max(0.)))
                });
            }
        }
        state.updating.set(false);
        self.report();
    }
    fn report(&self) {
        let state = self.ivars();
        if !state.observed.get() || state.updating.get() {
            return;
        }
        let bounds = self.contentView().bounds();
        let Some(document) = self.documentView() else {
            return;
        };
        let extent = document.frame().size;
        let (offset, viewport, total) = if state.horizontal.get() {
            (bounds.origin.x, bounds.size.width, extent.width)
        } else {
            (bounds.origin.y, bounds.size.height, extent.height)
        };
        let viewport = viewport.max(0.) as f32;
        let offset = (offset as f32).clamp(0., (total as f32 - viewport).max(0.));
        let value = (state.revision.get(), offset, viewport);
        if state.last.replace(Some(value)) != Some(value) {
            crate::events::emit(state.id, 10, offset, format!("{}:{}", value.0, viewport));
        }
    }
}
