use super::*;
use objc2::ClassType;
use objc2_foundation::{NSArray, NSIndexPath, NSObjectProtocol};
use objc2_ui_kit::{
    UICollectionView, UICollectionViewCell, UICollectionViewLayout,
    UICollectionViewLayoutAttributes,
};
use std::{cell::RefCell, rc::Rc};

#[derive(Default)]
struct Geometry {
    frames: Vec<CGRect>,
    size: CGSize,
}
pub(super) struct LayoutIvars {
    geometry: Rc<RefCell<Geometry>>,
}
define_class!(
    #[unsafe(super(UICollectionViewLayout))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeCollectionLayout"]
    #[ivars=LayoutIvars]
    struct CollectionLayout;
    impl CollectionLayout {
        #[unsafe(method(collectionViewContentSize))]
        fn content_size(&self) -> CGSize { self.ivars().geometry.borrow().size }
        #[unsafe(method_id(layoutAttributesForElementsInRect:))]
        fn attributes(&self, rect: CGRect) -> Retained<NSArray<UICollectionViewLayoutAttributes>> {
            let data = self.ivars().geometry.borrow();
            let attributes: Vec<_> = data.frames.iter().enumerate()
                .filter(|(_, f)| f.origin.y + f.size.height >= rect.origin.y && f.origin.y <= rect.origin.y + rect.size.height)
                .map(|(i, f)| attribute(i, *f, self.mtm())).collect();
            NSArray::from_retained_slice(&attributes)
        }
        #[unsafe(method_id(layoutAttributesForItemAtIndexPath:))]
        fn item(&self, path: &NSIndexPath) -> Option<Retained<UICollectionViewLayoutAttributes>> {
            let index: NSInteger = unsafe { msg_send![path, item] };
            self.ivars().geometry.borrow().frames.get(index as usize).map(|f| attribute(index as usize, *f, self.mtm()))
        }
        #[unsafe(method(shouldInvalidateLayoutForBoundsChange:))]
        fn bounds_changed(&self, bounds: CGRect) -> bool { bounds.size.width != self.ivars().geometry.borrow().size.width }
    }
);
fn attribute(
    index: usize,
    frame: CGRect,
    mtm: MainThreadMarker,
) -> Retained<UICollectionViewLayoutAttributes> {
    let path: Retained<NSIndexPath> = unsafe {
        msg_send![NSIndexPath::class(), indexPathForItem: index as NSInteger, inSection: 0isize]
    };
    let value = UICollectionViewLayoutAttributes::layoutAttributesForCellWithIndexPath(&path, mtm);
    value.setFrame(frame);
    value
}
struct Row {
    id: u32,
    view: Retained<UIView>,
}
pub(super) struct CollectionIvars {
    id: u32,
    rows: RefCell<Vec<Row>>,
    geometry: Rc<RefCell<Geometry>>,
    updating: Cell<bool>,
    revision: Cell<u32>,
    observed: Cell<bool>,
    last: Cell<Option<(u32, f32, f32)>>,
    cell_count: Cell<u32>,
}
define_class!(
    #[unsafe(super(UICollectionView))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeCollectionView"]
    #[ivars=CollectionIvars]
    pub(super) struct NativeCollection;
    impl NativeCollection {
        // Row buttons receive taps immediately, but must surrender the touch
        // when the collection's pan recognizer starts scrolling.
        #[unsafe(method(touchesShouldCancelInContentView:))]
        fn cancel_row_touch(&self, view: &UIView) -> bool {
            if view.isKindOfClass(UIButton::class()) {
                true
            } else {
                unsafe { msg_send![super(self), touchesShouldCancelInContentView: view] }
            }
        }
        #[unsafe(method(collectionView:numberOfItemsInSection:))]
        fn count(&self, _collection: &UICollectionView, _section: NSInteger) -> NSInteger {
            self.ivars().rows.borrow().len() as NSInteger
        }
        #[unsafe(method_id(collectionView:cellForItemAtIndexPath:))]
        fn cell(&self, collection: &UICollectionView, path: &NSIndexPath) -> Retained<UICollectionViewCell> {
            let cell = collection.dequeueReusableCellWithReuseIdentifier_forIndexPath(NSString::from_str("DotNativeRow").as_ref(), path);
            let index: NSInteger = unsafe { msg_send![path, item] };
            let identifier: Option<Retained<NSString>> = unsafe { msg_send![&cell, accessibilityIdentifier] };
            if identifier.is_none() {
                let count = self.ivars().cell_count.get() + 1;
                self.ivars().cell_count.set(count);
                let identifier = NSString::from_str(&format!("dotnative-virtual-cell-{}-{count}", self.ivars().id));
                unsafe { let _: () = msg_send![&cell, setAccessibilityIdentifier: &*identifier]; }
            }
            if let Some(row) = self.ivars().rows.borrow().get(index as usize) { attach(&cell, &row.view); }
            cell
        }
        #[unsafe(method(collectionView:didEndDisplayingCell:forItemAtIndexPath:))]
        fn ended(&self, _collection: &UICollectionView, cell: &UICollectionViewCell, _path: &NSIndexPath) {
            for view in cell.contentView().subviews() { view.removeFromSuperview(); }
        }
        #[unsafe(method(scrollViewDidScroll:))]
        fn scrolled(&self, _scroll: &UIScrollView) { self.report(); }
    }
);
fn attach(cell: &UICollectionViewCell, view: &UIView) {
    let content = cell.contentView();
    for old in content.subviews() {
        if &*old != view {
            old.removeFromSuperview();
        }
    }
    if view.superview().as_deref() != Some(&*content) {
        content.addSubview(view);
    }
    view.setAutoresizingMask(
        UIViewAutoresizing::FlexibleWidth | UIViewAutoresizing::FlexibleHeight,
    );
    view.setFrame(content.bounds());
}
impl NativeCollection {
    pub(super) fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let geometry = Rc::new(RefCell::new(Geometry::default()));
        let layout = CollectionLayout::alloc(mtm).set_ivars(LayoutIvars {
            geometry: geometry.clone(),
        });
        let layout: Retained<CollectionLayout> = unsafe { msg_send![super(layout), init] };
        let allocated = Self::alloc(mtm).set_ivars(CollectionIvars {
            id,
            rows: RefCell::new(Vec::new()),
            geometry,
            updating: Cell::new(true),
            revision: Cell::new(0),
            observed: Cell::new(false),
            last: Cell::new(None),
            cell_count: Cell::new(0),
        });
        let view: Retained<Self> = unsafe {
            msg_send![super(allocated), initWithFrame: CGRect::ZERO, collectionViewLayout: &*layout]
        };
        unsafe {
            let _: () = msg_send![&view, registerClass: UICollectionViewCell::class(), forCellWithReuseIdentifier: &*NSString::from_str("DotNativeRow")];
            let _: () = msg_send![&view, setDataSource: &*view];
            let _: () = msg_send![&view, setDelegate: &*view];
            let _: () = msg_send![&view, setPrefetchingEnabled: false];
        }
        view.setBackgroundColor(Some(&UIColor::clearColor()));
        view.setDelaysContentTouches(false);
        view.setScrollsToTop(false);
        view
    }
    pub(super) fn begin_update(&self) {
        self.ivars().updating.set(true);
    }
    pub(super) fn configure(
        &self,
        engine: &Engine,
        views: &HashMap<u32, Retained<UIView>>,
        node: &Node,
        new: bool,
    ) {
        let state = self.ivars();
        let document_id = node.children[0];
        let document = engine
            .node(document_id)
            .expect("validated virtual document");
        let old_offset = self.contentOffset();
        let changed = !state
            .rows
            .borrow()
            .iter()
            .map(|r| r.id)
            .eq(document.children.iter().copied());
        let rows = document
            .children
            .iter()
            .map(|id| Row {
                id: *id,
                view: views[id].clone(),
            })
            .collect();
        *state.rows.borrow_mut() = rows;
        let frame = engine.frame(document_id).expect("committed document");
        let frames: Vec<_> = document
            .children
            .iter()
            .map(|id| {
                let f = engine.frame(*id).expect("committed row");
                CGRect::new(
                    CGPoint::new(f.x as CGFloat, f.y as CGFloat),
                    CGSize::new(f.width as CGFloat, f.height as CGFloat),
                )
            })
            .collect();
        let size = CGSize::new(
            self.bounds().size.width,
            (frame.height as CGFloat).max(self.bounds().size.height),
        );
        let geometry_changed = {
            let data = state.geometry.borrow();
            data.frames != frames || data.size != size
        };
        *state.geometry.borrow_mut() = Geometry { frames, size };
        if changed {
            self.reloadData();
        }
        if geometry_changed {
            self.collectionViewLayout().invalidateLayout();
        }
        self.layoutIfNeeded();
        let offset = if new || state.revision.get() != node.scroll_position.0 {
            node.scroll_position.1 as CGFloat
        } else {
            old_offset.y
        };
        state.revision.set(node.scroll_position.0);
        state.observed.set(node.scroll_position.2);
        let target = CGPoint::new(
            0.,
            offset.clamp(0., (size.height - self.bounds().size.height).max(0.)),
        );
        if self.contentOffset() != target {
            self.setContentOffset(target);
        }
        self.layoutIfNeeded();
        // A row may change size without receiving a new cell or data-source callback.
        for path in self.indexPathsForVisibleItems() {
            let index: NSInteger = unsafe { msg_send![&*path, item] };
            if let (Some(cell), Some(row)) = (
                self.cellForItemAtIndexPath(&path),
                state.rows.borrow().get(index as usize),
            ) {
                attach(&cell, &row.view);
            }
        }
        state.updating.set(false);
        self.report();
    }
    fn report(&self) {
        let state = self.ivars();
        if state.updating.get() || !state.observed.get() {
            return;
        }
        let viewport = self.bounds().size.height.max(0.) as f32;
        let total = state.geometry.borrow().size.height as f32;
        let offset = (self.contentOffset().y as f32).clamp(0., (total - viewport).max(0.));
        let value = (state.revision.get(), offset, viewport);
        if state.last.replace(Some(value)) != Some(value) {
            crate::events::emit(state.id, 10, offset, format!("{}:{}", value.0, viewport));
        }
    }
}
