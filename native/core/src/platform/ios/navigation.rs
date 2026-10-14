//! UIKit owns transition progress and commits navigation only from didShow.
mod animator;
mod interaction;
use crate::{Engine, Node};
use objc2::{
    define_class, msg_send, rc::Retained, runtime::ProtocolObject, sel, DefinedClass,
    MainThreadMarker, MainThreadOnly, Message,
};
use objc2_core_foundation::{CGRect, CGSize};
use objc2_foundation::{NSArray, NSObjectProtocol, NSString};
use objc2_ui_kit::{
    NSDirectionalRectEdge, UIBarButtonItem, UIGestureRecognizerDelegate, UINavigationBar,
    UINavigationBarAppearance, UINavigationController, UINavigationControllerDelegate,
    UINavigationControllerOperation, UINavigationItemLargeTitleDisplayMode, UIScrollView,
    UIScrollViewContentInsetAdjustmentBehavior, UIView, UIViewController,
    UIViewControllerAnimatedTransitioning,
};
use std::{
    cell::{Cell, RefCell},
    collections::HashMap,
};

// The navigation item retains each action. Its target is self (UIKit does not
// retain targets); event delivery is queued, never reentrant into managed code.
struct ActionIvars {
    page: u32,
    index: usize,
}
define_class!(
    #[unsafe(super(UIBarButtonItem))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeToolbarAction"]
    #[ivars=ActionIvars]
    struct NativeAction;
    unsafe impl NSObjectProtocol for NativeAction {}
    impl NativeAction {
        #[unsafe(method(dotnativeInvoke:))]
        fn invoke(&self, _sender: &UIBarButtonItem) {
            crate::events::emit(self.ivars().page, 3, self.ivars().index as f32, String::new());
        }
    }
);
impl NativeAction {
    fn new(
        page: u32,
        index: usize,
        title: &str,
        mtm: MainThreadMarker,
    ) -> Retained<UIBarButtonItem> {
        let allocated = Self::alloc(mtm).set_ivars(ActionIvars { page, index });
        unsafe {
            let item: Retained<Self> = msg_send![super(allocated), initWithTitle: &*NSString::from_str(title), style: 0isize, target: std::ptr::null::<objc2::runtime::AnyObject>(), action: None::<objc2::runtime::Sel>];
            let _: () = msg_send![&item, setTarget: &*item];
            let _: () = msg_send![&item, setAction: sel!(dotnativeInvoke:)];
            Retained::into_super(item)
        }
    }
}

struct ScreenIvars {
    visible: Cell<bool>,
    root: Retained<UIView>,
    scroll: RefCell<Option<Retained<UIScrollView>>>,
    large: Cell<bool>,
    transition: Cell<animator::Spec>,
}

define_class!(
    #[unsafe(super(UIViewController))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeScreenController"]
    #[ivars=ScreenIvars]
    struct ScreenController;
    unsafe impl NSObjectProtocol for ScreenController {}
    impl ScreenController {
        #[unsafe(method(loadView))]
        fn load_view(&self) {
            self.setView(Some(&self.ivars().root));
        }
        #[unsafe(method(viewWillAppear:))]
        fn appearing(&self, animated: bool) {
            unsafe { let _: () = msg_send![super(self), viewWillAppear: animated]; }
            self.configure_scroll();
        }
    }
);
impl ScreenController {
    fn new(root: &UIView, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(ScreenIvars {
            visible: Cell::new(true),
            root: root.retain(),
            scroll: RefCell::new(None),
            large: Cell::new(false),
            transition: Cell::new(animator::Spec::default()),
        });
        unsafe { msg_send![super(allocated), init] }
    }

    fn configure_scroll(&self) {
        let scroll = self.ivars().scroll.borrow();
        self.setContentScrollView_forEdge(scroll.as_deref(), NSDirectionalRectEdge::Top);
        if let Some(scroll) = scroll.as_deref() {
            scroll.setScrollsToTop(true);
            scroll.setContentInsetAdjustmentBehavior(
                UIScrollViewContentInsetAdjustmentBehavior::Automatic,
            );
            scroll.setAlwaysBounceVertical(self.ivars().large.get());
        }
    }
}

pub(super) struct Ivars {
    id: u32,
    controller: Retained<UINavigationController>,
    pages: RefCell<HashMap<u32, Retained<UIViewController>>>,
    requested: RefCell<Vec<u32>>,
    transitioning: Cell<bool>,
    custom_animation: Cell<bool>,
    edge: RefCell<Option<Retained<objc2_ui_kit::UIPanGestureRecognizer>>>,
    interaction: RefCell<Option<Retained<objc2_ui_kit::UIPercentDrivenInteractiveTransition>>>,
    background: Cell<Option<[u8; 4]>>,
    headers: RefCell<HashMap<u32, crate::protocol::HeaderOptions>>,
}

define_class!(
    #[unsafe(super(UIView))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeNavigationView"]
    #[ivars=Ivars]
    pub(super) struct NavigationView;
    unsafe impl NSObjectProtocol for NavigationView {
        #[unsafe(method(respondsToSelector:))]
        fn responds(&self, selector: objc2::runtime::Sel) -> bool {
            if selector == sel!(navigationController:animationControllerForOperation:fromViewController:toViewController:)
                || selector == sel!(navigationController:interactionControllerForAnimationController:) {
                self.ivars().custom_animation.get()
            } else { unsafe { msg_send![super(self), respondsToSelector: selector] } }
        }
    }
    unsafe impl UIGestureRecognizerDelegate for NavigationView {
        #[unsafe(method(gestureRecognizerShouldBegin:))]
        fn gesture_should_begin(&self, recognizer: &objc2_ui_kit::UIGestureRecognizer) -> bool {
            self.can_begin_back_pan(recognizer)
        }
    }
    unsafe impl UINavigationControllerDelegate for NavigationView {
        #[unsafe(method_id(navigationController:interactionControllerForAnimationController:))]
        fn interaction_controller(&self, _nav: &UINavigationController,
            _animator: &ProtocolObject<dyn UIViewControllerAnimatedTransitioning>
        ) -> Option<Retained<ProtocolObject<dyn objc2_ui_kit::UIViewControllerInteractiveTransitioning>>> {
            self.ivars().interaction.borrow().as_ref().map(|driver| ProtocolObject::from_retained(driver.clone()))
        }
        #[unsafe(method_id(navigationController:animationControllerForOperation:fromViewController:toViewController:))]
        fn animator(&self, _nav: &UINavigationController, operation: UINavigationControllerOperation,
            from: &UIViewController, to: &UIViewController
        ) -> Option<Retained<ProtocolObject<dyn UIViewControllerAnimatedTransitioning>>> {
            (|| {
            let forward = operation == UINavigationControllerOperation::Push;
            let screen = if forward { to } else { from }.downcast_ref::<ScreenController>()?;
            let spec = screen.ivars().transition.get();
            if spec.kind == 0 { return None; }
            Some(ProtocolObject::from_retained(animator::Animator::new(spec, forward, self.ivars().interaction.borrow().is_some(), self.mtm())))
            })()
        }
        #[unsafe(method(navigationController:willShowViewController:animated:))]
        fn will_show(&self, nav: &UINavigationController, page: &UIViewController, animated: bool) {
            if let Some(screen) = page.downcast_ref::<ScreenController>() {
                nav.setNavigationBarHidden_animated(!screen.ivars().visible.get(), animated);
            }
            if animated {
                self.ivars().transitioning.set(true);
                crate::events::emit(self.ivars().id, 6, 0., String::new());
            }
        }
        #[unsafe(method(navigationController:didShowViewController:animated:))]
        fn did_show(&self, nav: &UINavigationController, _page: &UIViewController, _animated: bool) {
            self.ivars().transitioning.set(false);
            self.ivars().interaction.borrow_mut().take();
            self.sync_gestures();
            // A cancelled swipe reports the original depth: managed ownership
            // stays intact. No C# callbacks occur reentrantly inside UIKit.
            crate::events::emit(self.ivars().id, 5, nav.viewControllers().count() as f32, String::new());
        }
    }
    impl NavigationView {
        #[unsafe(method(dotnativeBackPan:))]
        fn back_pan(&self, gesture: &objc2_ui_kit::UIPanGestureRecognizer) {
            self.handle_back_pan(gesture);
        }
        #[unsafe(method(layoutSubviews))]
        fn layout_subviews(&self) {
            unsafe { let _: () = msg_send![super(self), layoutSubviews]; }
            let view = self.ivars().controller.view().expect("navigation controller view");
            view.setFrame(self.bounds());
        }
        #[unsafe(method(didMoveToWindow))]
        fn moved(&self) {
            unsafe { let _: () = msg_send![super(self), didMoveToWindow]; }
            let nav = &self.ivars().controller;
            if self.window().is_none() {
                nav.willMoveToParentViewController(None);
                if let Some(view) = nav.view() { view.removeFromSuperview(); }
                nav.removeFromParentViewController();
                return;
            }
            if nav.parentViewController().is_some() { return; }
            let mut next = self.nextResponder();
            while let Some(responder) = next {
                if let Some(parent) = responder.downcast_ref::<UIViewController>() {
                    parent.addChildViewController(nav);
                    let child_view = nav.view().expect("navigation controller view");
                    child_view.setFrame(self.bounds());
                    self.addSubview(&child_view);
                    nav.didMoveToParentViewController(Some(parent));
                    break;
                }
                next = responder.nextResponder();
            }
        }
    }
);

impl NavigationView {
    pub(super) fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let nav = UINavigationController::new(mtm);
        nav.navigationBar().setPrefersLargeTitles(true);
        let allocated = Self::alloc(mtm).set_ivars(Ivars {
            id,
            controller: nav,
            pages: RefCell::new(HashMap::new()),
            requested: RefCell::new(Vec::new()),
            transitioning: Cell::new(false),
            custom_animation: Cell::new(false),
            edge: RefCell::new(None),
            interaction: RefCell::new(None),
            background: Cell::new(None),
            headers: RefCell::new(HashMap::new()),
        });
        let view: Retained<Self> =
            unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] };
        unsafe {
            view.ivars()
                .controller
                .setDelegate(Some(ProtocolObject::from_ref(&*view)));
        }
        view.install_gesture();
        view
    }

    pub(super) fn sync(
        &self,
        engine: &Engine,
        views: &HashMap<u32, Retained<UIView>>,
        node: &Node,
    ) {
        let ivars = self.ivars();
        unsafe {
            let mode = node.control.focus_revision as isize;
            let current: isize = msg_send![&ivars.controller, overrideUserInterfaceStyle];
            if current != mode {
                let _: () = msg_send![&ivars.controller, setOverrideUserInterfaceStyle: mode];
            }
        }
        let background_changed = ivars.background.get() != Some(node.style.background);
        if background_changed {
            let appearance = UINavigationBarAppearance::new(self.mtm());
            appearance.configureWithOpaqueBackground();
            appearance.setBackgroundColor(Some(&super::color(node.style.background)));
            let bar = ivars.controller.navigationBar();
            bar.setStandardAppearance(&appearance);
            bar.setScrollEdgeAppearance(Some(&appearance));
            bar.setCompactAppearance(Some(&appearance));
            bar.setCompactScrollEdgeAppearance(Some(&appearance));
            ivars
                .controller
                .view()
                .unwrap()
                .setBackgroundColor(Some(&super::color(node.style.background)));
            ivars.background.set(Some(node.style.background));
        }
        let mut pages = ivars.pages.borrow_mut();
        for id in &node.children {
            let page = pages.entry(*id).or_insert_with(|| {
                let controller = ScreenController::new(&views[id], self.mtm());
                unsafe {
                    let _: () = msg_send![&controller, setEdgesForExtendedLayout: 0usize];
                }
                Retained::into_super(controller)
            });
            let model = engine.node(*id).unwrap();
            page.setTitle(Some(&NSString::from_str(&model.text)));
            // Preserve the registry-owned root and load it only when UIKit presents
            // the controller. Eager setView/configuration before navigation makes
            // iOS 26 compute a compact initial scroll inset for a large-title page.
            let screen = page.downcast_ref::<ScreenController>().unwrap();
            if node.children.last() == Some(id) {
                screen.ivars().transition.set(animator::Spec {
                    kind: node.navigation_transition,
                    duration: node.navigation_duration as f64,
                    easing: node.navigation_easing,
                });
            }
            let root_scroll = model
                .children
                .first()
                .and_then(|id| views.get(id))
                .and_then(|view| view.downcast_ref::<UIScrollView>());
            let scroll_changed = screen.ivars().scroll.borrow().as_deref() != root_scroll;
            let large_changed =
                screen.ivars().large.replace(model.header.large_title) != model.header.large_title;
            if scroll_changed {
                if let Some(old) = screen.ivars().scroll.borrow().as_ref() {
                    old.setScrollsToTop(false);
                }
                *screen.ivars().scroll.borrow_mut() = root_scroll.map(|scroll| scroll.retain());
            }
            if screen.isViewLoaded() && (scroll_changed || large_changed) {
                screen.configure_scroll();
            }
            page.downcast_ref::<ScreenController>()
                .unwrap()
                .ivars()
                .visible
                .set(model.header.visible);
            if background_changed
                || scroll_changed
                || ivars.headers.borrow().get(id) != Some(&model.header)
            {
                let extended =
                    root_scroll.is_some() || model.header.large_title || model.header.opacity < 1.;
                page.setExtendedLayoutIncludesOpaqueBars(extended);
                unsafe {
                    let _: () = msg_send![&**page, setEdgesForExtendedLayout: if extended { 15usize } else { 14usize }];
                }
                let item = page.navigationItem();
                let appearance = UINavigationBarAppearance::new(self.mtm());
                appearance.configureWithTransparentBackground();
                let mut background = node.style.background;
                background[3] = (background[3] as f32 * model.header.opacity).round() as u8;
                appearance.setBackgroundColor(Some(&super::color(background)));
                item.setStandardAppearance(Some(&appearance));
                // iOS 26 hosts large title content inside UIScrollView. An
                // opaque scroll-edge background can cover that native title
                // after a push; let the page surface show through at the edge.
                let scroll_edge = UINavigationBarAppearance::new(self.mtm());
                scroll_edge.configureWithTransparentBackground();
                item.setScrollEdgeAppearance(Some(if model.header.large_title {
                    &scroll_edge
                } else {
                    &appearance
                }));
                item.setCompactAppearance(Some(&appearance));
                item.setCompactScrollEdgeAppearance(Some(&appearance));
                item.setLargeTitleDisplayMode(if model.header.large_title {
                    UINavigationItemLargeTitleDisplayMode::Always
                } else {
                    UINavigationItemLargeTitleDisplayMode::Never
                });
                item.setHidesBackButton_animated(!model.header.back_visible, false);
                item.setLeftItemsSupplementBackButton(true);
                let actions: Vec<_> = model
                    .header
                    .actions
                    .iter()
                    .enumerate()
                    .map(|(index, title)| NativeAction::new(*id, index, title, self.mtm()))
                    .collect();
                item.setLeftBarButtonItems(Some(&NSArray::from_retained_slice(
                    &actions[..model.header.leading],
                )));
                // UIKit lays right items from the trailing edge inward.
                let right: Vec<_> = actions[model.header.leading..]
                    .iter()
                    .rev()
                    .cloned()
                    .collect();
                item.setRightBarButtonItems(Some(&NSArray::from_retained_slice(&right)));
                ivars.headers.borrow_mut().insert(*id, model.header.clone());
            }
        }
        pages.retain(|id, _| node.children.contains(id));
        ivars
            .headers
            .borrow_mut()
            .retain(|id, _| node.children.contains(id));
        if !ivars.transitioning.get() {
            if let Some(current) = ivars.controller.topViewController() {
                if let Some(screen) = current.downcast_ref::<ScreenController>() {
                    ivars
                        .controller
                        .setNavigationBarHidden_animated(!screen.ivars().visible.get(), false);
                    self.sync_gestures();
                }
            }
        }
        let target = node.children[..node.control.value as usize].to_vec();
        if *ivars.requested.borrow() == target || ivars.transitioning.get() {
            return;
        }
        let controllers: Vec<_> = target.iter().map(|id| pages[id].clone()).collect();
        let desired = NSArray::from_retained_slice(&controllers);
        let current = ivars.controller.viewControllers();
        // UIKit disables its interactive driver when the delegate advertises a
        // custom animator, even when that method returns nil. Refresh the cached
        // optional methods only when switching between stock/custom transitions.
        let custom = node.navigation_transition != 0;
        if ivars.custom_animation.replace(custom) != custom {
            unsafe {
                ivars.controller.setDelegate(None);
                ivars
                    .controller
                    .setDelegate(Some(ProtocolObject::from_ref(self)));
            }
        }
        let animated = current.count() != 0 && node.navigation_transition != 3;
        *ivars.requested.borrow_mut() = target;
        if current.isEqualToArray(&desired) {
            return;
        }
        let container = ivars.controller.view().expect("navigation controller view");
        container.setFrame(self.bounds());
        container.layoutIfNeeded();
        // Use UIKit's actual push/pop operations. Do not force layout after
        // starting a transition or wrap it in the renderer's disabled-actions transaction.
        if animated
            && desired.count() == current.count() + 1
            && (0..current.count()).all(|i| current.objectAtIndex(i) == desired.objectAtIndex(i))
        {
            ivars
                .controller
                .pushViewController_animated(&controllers[controllers.len() - 1], true);
        } else if animated
            && desired.count() < current.count()
            && (0..desired.count()).all(|i| desired.objectAtIndex(i) == current.objectAtIndex(i))
        {
            ivars
                .controller
                .popToViewController_animated(&controllers[controllers.len() - 1], true);
        } else {
            ivars
                .controller
                .setViewControllers_animated(&desired, false);
        }
    }
}

pub(super) fn bar_height(width: f32, mtm: MainThreadMarker) -> f32 {
    let bar = UINavigationBar::initWithFrame(UINavigationBar::alloc(mtm), CGRect::ZERO);
    bar.sizeThatFits(CGSize::new(width as _, 0.)).height as f32
}
