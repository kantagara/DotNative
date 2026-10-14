//! UIKit owns the transition context and calls didShow after completion.
use super::*;
use block2::RcBlock;
use objc2_core_foundation::CGPoint;
use objc2_foundation::NSObject;
use objc2_ui_kit::{
    UIAccessibilityIsReduceMotionEnabled, UITransitionContextFromViewControllerKey,
    UITransitionContextToViewControllerKey, UIViewAnimating, UIViewAnimatingPosition,
    UIViewControllerAnimatedTransitioning, UIViewControllerContextTransitioning,
    UIViewImplicitlyAnimating, UIViewPropertyAnimator,
};

#[derive(Clone, Copy)]
pub(super) struct Spec {
    pub kind: u8,
    pub duration: f64,
    pub easing: u8,
}
impl Default for Spec {
    fn default() -> Self {
        Self {
            kind: 0,
            duration: 0.18,
            easing: 1,
        }
    }
}

pub(super) struct AnimatorIvars {
    spec: Spec,
    forward: bool,
    interactive: bool,
    animation: RefCell<Option<Retained<UIViewPropertyAnimator>>>,
}
define_class!(
    #[unsafe(super(NSObject))]
    #[thread_kind=MainThreadOnly]
    #[name = "DotNativeNavigationAnimator"]
    #[ivars=AnimatorIvars]
    pub(super) struct Animator;
    unsafe impl NSObjectProtocol for Animator {}
    unsafe impl UIViewControllerAnimatedTransitioning for Animator {
        #[unsafe(method(transitionDuration:))]
        fn duration(
            &self,
            _context: Option<&ProtocolObject<dyn UIViewControllerContextTransitioning>>,
        ) -> f64 {
            self.animation_duration()
        }
        #[unsafe(method_id(interruptibleAnimatorForTransition:))]
        fn interruptible(
            &self,
            context: &ProtocolObject<dyn UIViewControllerContextTransitioning>,
        ) -> Retained<ProtocolObject<dyn UIViewImplicitlyAnimating>> {
            self.prepare(context);
            ProtocolObject::from_retained(
                self.ivars()
                    .animation
                    .borrow()
                    .as_ref()
                    .expect("prepared animator")
                    .clone(),
            )
        }
        #[unsafe(method(animationEnded:))]
        fn ended(&self, _completed: bool) {
            self.ivars().animation.borrow_mut().take();
        }
        #[unsafe(method(animateTransition:))]
        fn animate(&self, context: &ProtocolObject<dyn UIViewControllerContextTransitioning>) {
            self.prepare(context);
            let animation = self.ivars().animation.borrow().clone();
            if let Some(animation) = animation {
                animation.startAnimation();
            }
        }
    }
);
impl Animator {
    fn animation_duration(&self) -> f64 {
        if self.ivars().spec.kind == 3
            || (UIAccessibilityIsReduceMotionEnabled() && !self.ivars().interactive)
        {
            0.
        } else {
            self.ivars().spec.duration
        }
    }
    pub(super) fn new(
        spec: Spec,
        forward: bool,
        interactive: bool,
        mtm: MainThreadMarker,
    ) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(AnimatorIvars {
            spec,
            forward,
            interactive,
            animation: RefCell::new(None),
        });
        unsafe { msg_send![super(allocated), init] }
    }

    fn prepare(&self, context: &ProtocolObject<dyn UIViewControllerContextTransitioning>) {
        if self.ivars().animation.borrow().is_some() {
            return;
        }
        let from =
            unsafe { context.viewControllerForKey(UITransitionContextFromViewControllerKey) };
        let to = unsafe { context.viewControllerForKey(UITransitionContextToViewControllerKey) };
        let (Some(from), Some(to)) = (from, to) else {
            context.completeTransition(false);
            return;
        };
        let from_view = from.view().expect("outgoing navigation view");
        let to_view = to.view().expect("incoming navigation view");
        let container = context.containerView();
        let from_frame = context.initialFrameForViewController(&from);
        let to_frame = context.finalFrameForViewController(&to);
        let from_alpha = from_view.alpha();
        let to_alpha = to_view.alpha();
        let forward = self.ivars().forward;
        let fade = self.ivars().spec.kind == 2;
        to_view.setFrame(to_frame);
        let stationary = UIAccessibilityIsReduceMotionEnabled() && self.ivars().interactive;
        if !stationary && (forward || fade) {
            container.addSubview(&to_view);
        } else {
            container.insertSubview_belowSubview(&to_view, &from_view);
        }
        let duration = self.animation_duration();
        let width = container.bounds().size.width;
        let mut from_end = from_frame;
        let mut to_start = to_frame;
        if stationary || fade {
            // Under Reduce Motion this runs underneath the opaque outgoing
            // page: UIKit needs an animation to scrub, but no motion is visible.
            to_view.setAlpha(0.);
        } else {
            from_end.origin.x += if forward { -0.25 * width } else { width };
            to_start.origin.x += if forward { width } else { -0.25 * width };
            to_view.setFrame(to_start);
        }
        let moving_from = from_view.clone();
        let moving_to = to_view.clone();
        let animations = RcBlock::new(move || {
            moving_from.setFrame(from_end);
            moving_to.setFrame(to_frame);
            moving_to.setAlpha(to_alpha);
        });
        // x(t)=t; the y control points exactly reproduce the shared cubic curves.
        let (y1, y2) = match self.ivars().spec.easing {
            0 => (1. / 3., 2. / 3.),
            2 => (1., 1.),
            3 => (0., 0.),
            _ => (0., 1.),
        };
        let animation =
            UIViewPropertyAnimator::initWithDuration_controlPoint1_controlPoint2_animations(
                UIViewPropertyAnimator::alloc(self.mtm()),
                duration,
                CGPoint::new(1. / 3., y1),
                CGPoint::new(2. / 3., y2),
                Some(&animations),
            );
        let context = context.retain();
        animation.addCompletion(&RcBlock::new(move |_position: UIViewAnimatingPosition| {
            let completed = !context.transitionWasCancelled();
            from_view.setFrame(from_frame);
            from_view.setAlpha(from_alpha);
            to_view.setFrame(to_frame);
            to_view.setAlpha(to_alpha);
            if !completed {
                to_view.removeFromSuperview();
            }
            context.completeTransition(completed);
        }));
        *self.ivars().animation.borrow_mut() = Some(animation.clone());
    }
}
