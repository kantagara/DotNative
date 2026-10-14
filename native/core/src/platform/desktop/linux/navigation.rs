use super::*;
use std::rc::Rc;

#[derive(Default)]
pub(super) struct State {
    page: u32,
    outgoing: u32,
    depth: usize,
    busy: Rc<Cell<bool>>,
    callback: Option<gtk::TickCallbackId>,
    timer: Rc<RefCell<Option<glib::SourceId>>>,
    cancelled: Rc<Cell<bool>>,
}
impl Drop for State {
    fn drop(&mut self) {
        self.cancelled.set(true);
        if let Some(callback) = self.callback.take() {
            callback.remove();
        }
        if let Some(timer) = self.timer.borrow_mut().take() {
            timer.remove();
        }
    }
}
fn position(_parent: &gtk::Fixed, view: &Surface, x: f32) {
    // Presentation-only translation: GtkFixed child transforms affect minimum
    // size and can otherwise grow the window on every animation frame.
    view.imp().navigation_offset.set(x);
    view.queue_draw();
}
impl Registry {
    pub(super) fn sync_navigation(&mut self, engine: &Engine) {
        self.navigation.retain(|id, _| engine.node(*id).is_some());
        for (id, node) in engine.nodes().filter(|(_, n)| n.kind == Kind::Navigation) {
            let state = self.navigation.entry(id).or_default();
            let depth = node.control.value as usize;
            let target = node.children[depth - 1];
            for page in &node.children {
                self.entries[page].surface.set_visible(
                    *page == state.page || (state.busy.get() && *page == state.outgoing),
                );
            }
            if state.busy.get() {
                continue;
            }
            if let Some(callback) = state.callback.take() {
                callback.remove();
            }
            if let Some(timer) = state.timer.borrow_mut().take() {
                timer.remove();
            }
            if state.page == target {
                continue;
            }
            let from = self.entries.get(&state.page).map(|e| e.surface.clone());
            let to = self.entries[&target].surface.clone();
            let forward = depth > state.depth;
            state.outgoing = state.page;
            state.page = target;
            state.depth = depth;
            to.set_visible(true);
            let reduced = gtk::Settings::default().is_some_and(|s| !s.is_gtk_enable_animations());
            if from.is_none() || node.navigation_transition == 3 || reduced {
                if let Some(from) = from {
                    from.set_visible(false);
                }
                crate::events::emit(id, 5, depth as f32, String::new());
                continue;
            }
            let from = from.unwrap();
            let container = &self.entries[&id].content;
            let gate = self.entries[&id].surface.clone();
            gate.set_overflow(gtk::Overflow::Hidden);
            gate.set_can_target(false);
            if let Some(window) = gate.root().and_downcast::<gtk::Window>() {
                GtkWindowExt::set_focus(&window, None::<&gtk::Widget>);
            }
            state.busy.set(true);
            crate::events::emit(id, 6, 0., String::new());
            let fade = node.navigation_transition == 2;
            let width = engine.frame(id).map_or(0., |f| f.width);
            let initial_x = if fade {
                0.
            } else if forward {
                width
            } else {
                -0.25 * width
            };
            position(container, &to, initial_x);
            to.set_opacity(if fade { 0. } else { 1. });
            if forward || fade {
                to.insert_before(container, None::<&gtk::Widget>);
            } else {
                from.insert_before(container, None::<&gtk::Widget>);
            }
            let parent = container.downgrade();
            let gate = gate.downgrade();
            let busy = state.busy.clone();
            let duration = node.navigation_duration as f64;
            let easing = node.navigation_easing;
            let cancelled = state.cancelled.clone();
            let update = Rc::new(move |t: f32| {
                if cancelled.get() || !busy.get() {
                    return glib::ControlFlow::Break;
                }
                let Some(parent) = parent.upgrade() else {
                    return glib::ControlFlow::Break;
                };
                let p = match easing {
                    0 => t,
                    2 => 1. - (1. - t).powi(3),
                    3 => t * t * t,
                    _ => t * t * (3. - 2. * t),
                };
                let width = parent.width() as f32;
                if fade {
                    to.set_opacity(p as f64);
                } else {
                    position(
                        &parent,
                        &from,
                        p * if forward { -0.25 * width } else { width },
                    );
                    position(
                        &parent,
                        &to,
                        (1. - p) * if forward { width } else { -0.25 * width },
                    );
                }
                if t >= 1. {
                    from.set_visible(false);
                    position(&parent, &from, 0.);
                    position(&parent, &to, 0.);
                    to.set_opacity(1.);
                    if let Some(gate) = gate.upgrade() {
                        gate.set_can_target(true);
                    }
                    busy.set(false);
                    crate::events::emit(id, 5, depth as f32, String::new());
                    glib::ControlFlow::Break
                } else {
                    glib::ControlFlow::Continue
                }
            });
            let started = std::time::Instant::now();
            let tick = update.clone();
            state.callback = Some(container.add_tick_callback(move |_, _| {
                tick((started.elapsed().as_secs_f64() / duration).clamp(0., 1.) as f32)
            }));
            // A hidden window has no GTK frame ticks. Complete ownership transfer
            // after the deadline even if the window is minimized mid-transition.
            let timer = state.timer.clone();
            *state.timer.borrow_mut() = Some(glib::timeout_add_local_once(
                std::time::Duration::from_secs_f64(duration + 0.1),
                move || {
                    timer.borrow_mut().take();
                    update(1.);
                },
            ));
        }
    }
}
