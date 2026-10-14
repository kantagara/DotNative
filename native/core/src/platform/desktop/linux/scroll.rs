use super::*;

struct State {
    id: u32,
    revision: Cell<Option<u32>>,
    observed: Cell<bool>,
    horizontal: Cell<bool>,
    contain: Cell<bool>,
    suppress: Rc<Cell<bool>>,
    last: Cell<Option<(u32, f32, f32)>>,
}
impl State {
    fn report(&self, adjustment: &gtk::Adjustment) {
        if !self.observed.get() || self.suppress.get() {
            return;
        }
        let Some(revision) = self.revision.get() else {
            return;
        };
        let viewport = adjustment.page_size().max(0.) as f32;
        let offset =
            (adjustment.value() as f32).clamp(0., (adjustment.upper() as f32 - viewport).max(0.));
        let value = (revision, offset, viewport);
        if self.last.replace(Some(value)) != Some(value) {
            crate::events::emit(self.id, 10, offset, format!("{revision}:{viewport}"));
        }
    }
}
pub(super) struct Binding {
    view: gtk::ScrolledWindow,
    state: Rc<State>,
}
impl Binding {
    pub(super) fn new(
        view: &gtk::ScrolledWindow,
        host: &Surface,
        id: u32,
        suppress: Rc<Cell<bool>>,
    ) -> Self {
        let state = Rc::new(State {
            id,
            revision: Cell::new(None),
            observed: Cell::new(false),
            horizontal: Cell::new(false),
            contain: Cell::new(true),
            suppress,
            last: Cell::new(None),
        });
        for (horizontal, adjustment) in [(true, view.hadjustment()), (false, view.vadjustment())] {
            let s = state.clone();
            adjustment.connect_value_changed(move |a| {
                if s.horizontal.get() == horizontal {
                    s.report(a);
                }
            });
            let s = state.clone();
            adjustment.connect_changed(move |a| {
                if s.horizontal.get() == horizontal {
                    s.report(a);
                }
            });
        }
        // Bubble runs after the ScrolledWindow's native handler. Consume only
        // an unhandled boundary gesture when the list requests containment.
        let tap = gtk::GestureClick::new();
        let tap_id = id;
        tap.connect_released(move |_, _, _, y| {
            crate::events::emit(tap_id, 12, y as f32, String::new())
        });
        view.add_controller(tap);
        let wheel = gtk::EventControllerScroll::new(gtk::EventControllerScrollFlags::BOTH_AXES);
        wheel.set_propagation_phase(gtk::PropagationPhase::Bubble);
        let s = state.clone();
        wheel.connect_scroll(move |_, _, _| {
            if s.contain.get() {
                glib::Propagation::Stop
            } else {
                glib::Propagation::Proceed
            }
        });
        host.add_controller(wheel);
        Self {
            view: view.clone(),
            state,
        }
    }
    pub(super) fn configure(&self, node: &Node, total: f64, viewport: f64) {
        let s = &self.state;
        s.horizontal.set(node.scroll_axis == 1);
        s.contain.set(node.contain_scroll);
        s.observed.set(node.scroll_position.2);
        let adjustment = self.adjustment();
        let requested =
            s.revision.replace(Some(node.scroll_position.0)) != Some(node.scroll_position.0);
        let offset = if requested {
            node.scroll_position.1 as f64
        } else {
            adjustment.value()
        };
        let viewport = viewport.max(0.);
        // Setting the current value on every managed commit cancels GTK's
        // ongoing wheel/kinetic animation. Only explicit requests move it.
        let total = total.max(viewport);
        if adjustment.upper() != total {
            adjustment.set_upper(total);
        }
        if adjustment.page_size() != viewport {
            adjustment.set_page_size(viewport);
        }
        if adjustment.step_increment() != 40. {
            adjustment.set_step_increment(40.);
        }
        if adjustment.page_increment() != viewport * 0.9 {
            adjustment.set_page_increment(viewport * 0.9);
        }
        if requested {
            adjustment.set_value(offset.clamp(0., (total - viewport).max(0.)));
        }
    }
    fn adjustment(&self) -> gtk::Adjustment {
        if self.state.horizontal.get() {
            self.view.hadjustment()
        } else {
            self.view.vadjustment()
        }
    }
    pub(super) fn report(&self) {
        self.state.report(&self.adjustment());
    }
}
