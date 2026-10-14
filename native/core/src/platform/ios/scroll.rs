use super::*;
use objc2_ui_kit::UITapGestureRecognizer;

pub(super) struct ScrollIvars {
    id: u32,
    revision: Cell<u32>,
    observed: Cell<bool>,
    horizontal: Cell<bool>,
    updating: Cell<bool>,
    last: Cell<Option<(u32, f32, f32)>>,
}
define_class!(
    #[unsafe(super(UIScrollView))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeScrollView"]
    #[ivars=ScrollIvars]
    pub(super) struct NativeScroll;
    impl NativeScroll {
        #[unsafe(method(tapped:))]
        fn tapped(&self, gesture: &UITapGestureRecognizer) {
            let point = gesture.locationInView(Some(self));
            crate::events::emit(self.ivars().id,12,(point.y-self.bounds().origin.y) as f32,String::new());
        }
        #[unsafe(method(scrollViewWillBeginDragging:))]
        fn begin_dragging(&self, _scroll: &UIScrollView) {
            crate::events::emit(self.ivars().id,13,1.,String::new());
        }
        #[unsafe(method(scrollViewDidEndDragging:willDecelerate:))]
        fn end_dragging(&self, _scroll: &UIScrollView, decelerate: bool) {
            if !decelerate { crate::events::emit(self.ivars().id,13,0.,String::new()); }
        }
        #[unsafe(method(scrollViewDidEndDecelerating:))]
        fn end_decelerating(&self, _scroll: &UIScrollView) {
            crate::events::emit(self.ivars().id,13,0.,String::new());
        }
        #[unsafe(method(scrollViewDidScroll:))]
        fn scrolled(&self, _scroll: &UIScrollView) { self.report(); }
    }
);
impl NativeScroll {
    pub(super) fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(ScrollIvars {
            id,
            revision: Cell::new(0),
            observed: Cell::new(false),
            horizontal: Cell::new(false),
            updating: Cell::new(true),
            last: Cell::new(None),
        });
        let scroll: Retained<Self> =
            unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] };
        unsafe {
            let _: () = msg_send![&scroll, setDelegate: &*scroll];
        }
        let tap: Retained<UITapGestureRecognizer> = unsafe {
            msg_send![UITapGestureRecognizer::alloc(mtm), initWithTarget: &*scroll, action: sel!(tapped:)]
        };
        tap.setCancelsTouchesInView(false);
        scroll.addGestureRecognizer(&tap);
        scroll.setDelaysContentTouches(false);
        scroll.setScrollsToTop(false);
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
            let inset = self.adjustedContentInset();
            let offset = node.scroll_position.1 as CGFloat;
            let point = if state.horizontal.get() {
                let viewport = (self.bounds().size.width - inset.left - inset.right).max(0.);
                CGPoint::new(
                    offset.min((self.contentSize().width - viewport).max(0.)) - inset.left,
                    -inset.top,
                )
            } else {
                let viewport = (self.bounds().size.height - inset.top - inset.bottom).max(0.);
                CGPoint::new(
                    -inset.left,
                    offset.min((self.contentSize().height - viewport).max(0.)) - inset.top,
                )
            };
            self.setContentOffset(point);
        }
        state.updating.set(false);
        self.report();
    }
    fn report(&self) {
        let state = self.ivars();
        if !state.observed.get() || state.updating.get() {
            return;
        }
        let inset = self.adjustedContentInset();
        let (offset, viewport, total) = if state.horizontal.get() {
            (
                self.contentOffset().x + inset.left,
                self.bounds().size.width - inset.left - inset.right,
                self.contentSize().width,
            )
        } else {
            (
                self.contentOffset().y + inset.top,
                self.bounds().size.height - inset.top - inset.bottom,
                self.contentSize().height,
            )
        };
        let viewport = viewport.max(0.) as f32;
        let offset = (offset as f32).clamp(0., (total as f32 - viewport).max(0.));
        let value = (state.revision.get(), offset, viewport);
        if state.last.replace(Some(value)) != Some(value) {
            crate::events::emit(state.id, 10, offset, format!("{}:{}", value.0, viewport));
        }
    }
}
