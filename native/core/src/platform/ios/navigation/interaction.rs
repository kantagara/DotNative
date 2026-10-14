use super::*;
use objc2_ui_kit::{
    UIGestureRecognizerState, UIPanGestureRecognizer, UIPercentDrivenInteractiveTransition,
};

impl NavigationView {
    pub(super) fn can_begin_back_pan(
        &self,
        recognizer: &objc2_ui_kit::UIGestureRecognizer,
    ) -> bool {
        let Some(pan) = recognizer.downcast_ref::<objc2_ui_kit::UIPanGestureRecognizer>() else {
            return false;
        };
        let view = self.ivars().controller.view().unwrap();
        let velocity = pan.velocityInView(Some(&view));
        let start = pan.locationInView(Some(&view)).x - pan.translationInView(Some(&view)).x;
        !self.ivars().transitioning.get()
            && self.ivars().controller.viewControllers().count() > 1
            && start <= 24.
            && velocity.x > 0.
            && velocity.x.abs() > velocity.y.abs()
    }

    pub(super) fn install_gesture(&self) {
        // A bounded pan accepts the same 24-point edge region at any screen scale.
        let gesture = unsafe {
            UIPanGestureRecognizer::initWithTarget_action(
                UIPanGestureRecognizer::alloc(self.mtm()),
                Some(self),
                Some(sel!(dotnativeBackPan:)),
            )
        };
        gesture.setMaximumNumberOfTouches(1);
        gesture.setDelegate(Some(ProtocolObject::from_ref(self)));
        gesture.setEnabled(false);
        self.ivars()
            .controller
            .view()
            .unwrap()
            .addGestureRecognizer(&gesture);
        *self.ivars().edge.borrow_mut() = Some(gesture);
    }

    pub(super) fn sync_gestures(&self) {
        let nav = &self.ivars().controller;
        let kind = nav
            .topViewController()
            .and_then(|page| {
                page.downcast_ref::<ScreenController>()
                    .map(|screen| screen.ivars().transition.get().kind)
            })
            .unwrap_or(0);
        if let Some(gesture) = nav.interactivePopGestureRecognizer() {
            gesture.setEnabled(kind == 0);
        }
        if nav.respondsToSelector(sel!(interactiveContentPopGestureRecognizer)) {
            if let Some(gesture) = nav.interactiveContentPopGestureRecognizer() {
                gesture.setEnabled(kind == 0);
            }
        }
        if let Some(gesture) = self.ivars().edge.borrow().as_ref() {
            gesture.setEnabled(matches!(kind, 1 | 2) && nav.viewControllers().count() > 1);
        }
    }

    pub(super) fn handle_back_pan(&self, gesture: &UIPanGestureRecognizer) {
        let ivars = self.ivars();
        let view = ivars.controller.view().unwrap();
        let progress = (gesture.translationInView(Some(&view)).x
            / view.bounds().size.width.max(1.))
        .clamp(0., 1.);
        match gesture.state() {
            UIGestureRecognizerState::Began => {
                if ivars.transitioning.get() || ivars.controller.viewControllers().count() <= 1 {
                    return;
                }
                let driver = UIPercentDrivenInteractiveTransition::new(self.mtm());
                driver.setWantsInteractiveStart(true);
                *ivars.interaction.borrow_mut() = Some(driver);
                ivars.controller.popViewControllerAnimated(true);
            }
            UIGestureRecognizerState::Changed => {
                if let Some(driver) = ivars.interaction.borrow().as_ref() {
                    driver.updateInteractiveTransition(progress);
                }
            }
            UIGestureRecognizerState::Ended
            | UIGestureRecognizerState::Cancelled
            | UIGestureRecognizerState::Failed => {
                let driver = ivars.interaction.borrow().clone();
                if let Some(driver) = driver {
                    let velocity = gesture.velocityInView(Some(&view)).x;
                    if gesture.state() == UIGestureRecognizerState::Ended
                        && (progress > 0.5 || (progress > 0.05 && velocity > 500.))
                    {
                        driver.finishInteractiveTransition();
                    } else {
                        driver.cancelInteractiveTransition();
                    }
                }
            }
            _ => (),
        }
    }
}
