//! GTK4 objects are reference counted and confined to GTK's initializing thread.
//! Styles remain typed values. A fixed GTK reset prevents double-painted button chrome.
mod navigation;
#[cfg(feature = "dev-host")]
mod radio_smoke;
mod scroll;
#[cfg(feature = "dev-host")]
mod theme;
#[cfg(feature = "dev-host")]
mod virtual_list_smoke;
use crate::{AvailableSpace, Engine, Error, Frame, Kind, Measure, Node, Result, Size, Style};
use gtk::{glib, prelude::*, subclass::prelude::*};
use std::{
    cell::{Cell, RefCell},
    collections::HashMap,
    sync::Arc,
};

mod surface {
    use super::*;

    #[derive(Default)]
    pub struct Surface {
        pub style: Cell<Style>,
        pub navigation_offset: Cell<f32>,
        pub decoration: Cell<crate::protocol::BoxDecoration>,
        pub visual: Cell<crate::protocol::VisualStyle>,
        pub gradient: RefCell<Option<crate::protocol::Gradient>>,
        pub image: RefCell<Option<gtk::gdk::Texture>>,
        pub background_image: RefCell<Option<gtk::gdk::Texture>>,
        pub background_fit: Cell<u8>,
        pub image_fit: Cell<u8>,
        pub image_tint: Cell<Option<[u8; 4]>>,
    }

    #[glib::object_subclass]
    impl ObjectSubclass for Surface {
        const NAME: &'static str = "DotNativeSurface";
        type Type = super::Surface;
        type ParentType = gtk::Fixed;
    }
    impl ObjectImpl for Surface {}
    impl FixedImpl for Surface {}
    impl WidgetImpl for Surface {
        fn snapshot(&self, snapshot: &gtk::Snapshot) {
            let view = self.obj();
            snapshot.save();
            snapshot.translate(&gtk::graphene::Point::new(self.navigation_offset.get(), 0.));
            let style = self.style.get();
            let bounds =
                gtk::graphene::Rect::new(0., 0., view.width() as f32, view.height() as f32);
            let visual = self.visual.get();
            let geometry = crate::box_geometry::BoxGeometry::styles(
                self.decoration.get(),
                style.radius,
                visual.border_width,
                visual.border_color,
                bounds.width(),
                bounds.height(),
            );
            let r = geometry.radii.map(|r| gtk::graphene::Size::new(r, r));
            let outline = gtk::gsk::RoundedRect::new(bounds, r[0], r[1], r[2], r[3]);
            if visual.clip {
                snapshot.push_rounded_clip(&outline);
            }
            snapshot.append_outset_shadow(
                &outline,
                &rgba(visual.shadow_color),
                visual.shadow_x,
                visual.shadow_y,
                0.,
                visual.shadow_blur,
            );
            snapshot.push_rounded_clip(&outline);
            let c = style.background;
            snapshot.append_color(
                &gtk::gdk::RGBA::new(
                    c[0] as f32 / 255.,
                    c[1] as f32 / 255.,
                    c[2] as f32 / 255.,
                    c[3] as f32 / 255.,
                ),
                &bounds,
            );
            if let Some(g) = self.gradient.borrow().as_ref() {
                let point = |p: [f32; 2]| {
                    gtk::graphene::Point::new(p[0] * bounds.width(), p[1] * bounds.height())
                };
                let stops: Vec<_> = g
                    .stops
                    .iter()
                    .map(|(offset, color)| gtk::gsk::ColorStop::new(*offset, rgba(*color)))
                    .collect();
                match g.kind {
                    2 => {
                        let radius = g.end[0] * bounds.width().min(bounds.height());
                        if radius > 0. {
                            snapshot.append_radial_gradient(
                                &bounds,
                                &point(g.start),
                                radius,
                                radius,
                                0.,
                                1.,
                                &stops,
                            );
                        }
                    }
                    3 => snapshot.append_conic_gradient(&bounds, &point(g.start), 0., &stops),
                    _ => snapshot.append_linear_gradient(
                        &bounds,
                        &point(g.start),
                        &point(g.end),
                        &stops,
                    ),
                }
            }
            if let Some(image) = self.background_image.borrow().as_ref() {
                let r = crate::image_fit::rect(
                    self.background_fit.get(),
                    [image.width() as f32, image.height() as f32],
                    [bounds.width(), bounds.height()],
                );
                if r[2] > 0. && r[3] > 0. {
                    snapshot
                        .append_texture(image, &gtk::graphene::Rect::new(r[0], r[1], r[2], r[3]));
                }
            }
            snapshot.pop();
            if let Some(image) = self.image.borrow().as_ref() {
                let r = crate::image_fit::rect(
                    self.image_fit.get(),
                    [image.width() as f32, image.height() as f32],
                    [bounds.width(), bounds.height()],
                );
                if let Some(c) = self.image_tint.get() {
                    let mut matrix = [0.; 16];
                    matrix[15] = c[3] as f32 / 255.;
                    snapshot.push_color_matrix(
                        &gtk::graphene::Matrix::from_float(matrix),
                        &gtk::graphene::Vec4::new(
                            c[0] as f32 / 255.,
                            c[1] as f32 / 255.,
                            c[2] as f32 / 255.,
                            0.,
                        ),
                    );
                }
                if r[2] > 0. && r[3] > 0. {
                    snapshot
                        .append_texture(image, &gtk::graphene::Rect::new(r[0], r[1], r[2], r[3]));
                }
                if self.image_tint.get().is_some() {
                    snapshot.pop();
                }
            }
            self.parent_snapshot(snapshot);
            draw_borders(snapshot, &bounds, &geometry);
            if visual.clip {
                snapshot.pop();
            }
            snapshot.restore();
        }
    }
}
glib::wrapper! {
    pub struct Surface(ObjectSubclass<surface::Surface>)
        @extends gtk::Widget, gtk::Fixed,
        @implements gtk::Accessible, gtk::Buildable, gtk::ConstraintTarget;
}

use std::rc::Rc;

fn cairo_path(ctx: &gtk::cairo::Context, path: &crate::box_geometry::Path) {
    use crate::box_geometry::Segment::*;
    ctx.new_path();
    for command in path {
        match *command {
            Move(x, y) => ctx.move_to(x as f64, y as f64),
            Line(x, y) => ctx.line_to(x as f64, y as f64),
            Curve([a, b, c, d, e, f]) => {
                ctx.curve_to(a as f64, b as f64, c as f64, d as f64, e as f64, f as f64)
            }
            Close => ctx.close_path(),
        }
    }
}
fn draw_borders(
    snapshot: &gtk::Snapshot,
    bounds: &gtk::graphene::Rect,
    geometry: &crate::box_geometry::BoxGeometry,
) {
    if geometry
        .borders
        .iter()
        .all(|b| b.width == 0. || b.color[3] == 0)
    {
        return;
    }
    let ctx = snapshot.append_cairo(bounds);
    for (i, border) in geometry.borders.iter().enumerate() {
        if border.width <= 0. || border.color[3] == 0 {
            continue;
        }
        ctx.save().expect("cairo save");
        cairo_path(&ctx, &geometry.ring);
        ctx.set_fill_rule(gtk::cairo::FillRule::EvenOdd);
        ctx.clip();
        ctx.set_antialias(gtk::cairo::Antialias::None);
        cairo_path(&ctx, &geometry.wedges[i]);
        ctx.clip();
        ctx.set_antialias(gtk::cairo::Antialias::Default);
        let [r, g, b, a] = border.color.map(|v| v as f64 / 255.);
        ctx.set_source_rgba(r, g, b, a);
        if border.style == 0 {
            ctx.paint().expect("border fill");
        } else {
            cairo_path(&ctx, &geometry.center);
            let w = border.width as f64;
            ctx.set_line_width(w);
            let dash = if border.style == 1 {
                [w * 3., w * 2.]
            } else {
                [w * 0.001, w * 2.]
            };
            ctx.set_dash(&dash, 0.);
            ctx.set_line_cap(if border.style == 2 {
                gtk::cairo::LineCap::Round
            } else {
                gtk::cairo::LineCap::Butt
            });
            ctx.stroke().expect("border stroke");
        }
        ctx.restore().expect("cairo restore");
    }
}
fn rgba(c: [u8; 4]) -> gtk::gdk::RGBA {
    gtk::gdk::RGBA::new(
        c[0] as f32 / 255.,
        c[1] as f32 / 255.,
        c[2] as f32 / 255.,
        c[3] as f32 / 255.,
    )
}
fn attributes(node: &Node) -> gtk::pango::AttrList {
    let attrs = gtk::pango::AttrList::new();
    attrs.insert(gtk::pango::AttrSize::new_size_absolute(
        (node.style.font_size * gtk::pango::SCALE as f32) as i32,
    ));
    if !node.font_axes.is_empty() {
        let mut description = gtk::pango::FontDescription::new();
        let axes = node
            .font_axes
            .iter()
            .map(|(tag, value)| format!("{}={value}", std::str::from_utf8(tag).unwrap()))
            .collect::<Vec<_>>()
            .join(",");
        description.set_variations(Some(&axes));
        attrs.insert(gtk::pango::AttrFontDesc::new(&description));
    }
    let t = &node.typography;
    attrs.insert(gtk::pango::AttrInt::new_letter_spacing(
        (t.letter_spacing * gtk::pango::SCALE as f32) as i32,
    ));
    if t.line_height > 0. {
        attrs.insert(gtk::pango::AttrInt::new_line_height_absolute(
            (t.line_height * gtk::pango::SCALE as f32) as i32,
        ));
    }
    attrs.insert(gtk::pango::AttrInt::new_underline(
        if t.decoration & 1 != 0 {
            gtk::pango::Underline::Single
        } else {
            gtk::pango::Underline::None
        },
    ));
    attrs.insert(gtk::pango::AttrInt::new_strikethrough(
        t.decoration & 2 != 0,
    ));
    attrs.insert(gtk::pango::AttrInt::new_weight(match t.weight {
        100 => gtk::pango::Weight::Thin,
        200 => gtk::pango::Weight::Ultralight,
        300 => gtk::pango::Weight::Light,
        500 => gtk::pango::Weight::Medium,
        600 => gtk::pango::Weight::Semibold,
        700 => gtk::pango::Weight::Bold,
        800 => gtk::pango::Weight::Ultrabold,
        900 => gtk::pango::Weight::Heavy,
        _ => gtk::pango::Weight::Normal,
    }));
    attrs.insert(gtk::pango::AttrInt::new_style(if t.italic {
        gtk::pango::Style::Italic
    } else {
        gtk::pango::Style::Normal
    }));
    if !t.family.is_empty() {
        attrs.insert(gtk::pango::AttrString::new_family(&t.family));
    }
    let c = node.style.foreground;
    attrs.insert(gtk::pango::AttrColor::new_foreground(
        c[0] as u16 * 257,
        c[1] as u16 * 257,
        c[2] as u16 * 257,
    ));
    attrs.insert(gtk::pango::AttrInt::new_foreground_alpha(c[3] as u16 * 257));
    let mut offset = 0u32;
    for run in node.text_runs.iter() {
        let styled = node.run_style(run);
        let end = offset + run.text.len() as u32;
        let run_attrs = attributes(&styled);
        for mut attribute in run_attrs.attributes() {
            attribute.set_start_index(offset);
            attribute.set_end_index(end);
            attrs.change(attribute);
        }
        offset = end;
    }
    attrs
}
fn configure_label(label: &gtk::Label, node: &Node) {
    label.set_text(&node.text);
    label.set_attributes(Some(&attributes(node)));
    let t = &node.typography;
    label.set_xalign(match node.text_alignment() {
        1 => 0.5,
        2 => 1.,
        _ => 0.,
    });
    label.set_justify(match node.text_alignment() {
        1 => gtk::Justification::Center,
        2 => gtk::Justification::Right,
        3 => gtk::Justification::Fill,
        _ => gtk::Justification::Left,
    });
    label.set_lines(if t.max_lines <= 0 { -1 } else { t.max_lines });
    let mode = if t.overflow == 0 {
        if node.kind == Kind::Text && t.max_lines <= 0 {
            1
        } else {
            5
        }
    } else {
        t.overflow
    };
    label.set_wrap(mode == 1 || t.max_lines > 1);
    label.set_ellipsize(match mode {
        3 => gtk::pango::EllipsizeMode::Start,
        4 => gtk::pango::EllipsizeMode::Middle,
        5 => gtk::pango::EllipsizeMode::End,
        _ => gtk::pango::EllipsizeMode::None,
    });
}
fn image(node: &Node) -> Result<gtk::Image> {
    if node.kind == Kind::Icon {
        return Ok(gtk::Image::from_icon_name(match &*node.text {
            "star" => "starred-symbolic",
            "check" => "emblem-ok-symbolic",
            "info" => "dialog-information-symbolic",
            _ => return Err(Error::Measurement("unknown icon")),
        }));
    }
    let loader = gtk::gdk_pixbuf::PixbufLoader::new();
    loader
        .write(&node.image)
        .map_err(|_| Error::Measurement("invalid image"))?;
    loader
        .close()
        .map_err(|_| Error::Measurement("invalid image"))?;
    let pixbuf = loader.pixbuf().ok_or(Error::Measurement("empty image"))?;
    Ok(gtk::Image::from_pixbuf(Some(&pixbuf)))
}
fn header(node: &Node, id: u32) -> gtk::HeaderBar {
    let bar = gtk::HeaderBar::new();
    bar.set_show_title_buttons(false);
    let title = gtk::Label::new(Some(&node.text));
    configure_label(&title, node);
    title.set_ellipsize(gtk::pango::EllipsizeMode::End);
    bar.set_title_widget(Some(&title));
    if node.header.can_pop && node.header.back_visible {
        let back = gtk::Button::from_icon_name("go-previous-symbolic");
        back.set_tooltip_text(Some("Back"));
        back.connect_clicked(move |_| crate::events::emit(id, 3, -1., String::new()));
        bar.pack_start(&back);
    }
    for (index, action) in node.header.actions.iter().enumerate() {
        let button = gtk::Button::with_label(action);
        if let Some(label) = button.child().and_downcast::<gtk::Label>() {
            label.set_attributes(Some(&attributes(node)));
        }
        button.connect_clicked(move |_| crate::events::emit(id, 3, index as f32, String::new()));
        if index < node.header.leading {
            bar.pack_start(&button);
        } else {
            bar.pack_end(&button);
        }
    }
    // HeaderBar and its buttons otherwise retain the OS theme's opaque fills,
    // which can disagree with the app's explicitly resolved foreground.
    let [r, g, b, a] = node.style.foreground;
    let palette = gtk::CssProvider::new();
    palette.load_from_data(&format!(
        "* {{ color: rgba({r}, {g}, {b}, {}); background: transparent; box-shadow: none; }}",
        a as f32 / 255.
    ));
    fn apply_palette(widget: &gtk::Widget, palette: &gtk::CssProvider) {
        #[allow(deprecated)]
        widget
            .style_context()
            .add_provider(palette, gtk::STYLE_PROVIDER_PRIORITY_APPLICATION);
        let mut child = widget.first_child();
        while let Some(current) = child {
            apply_palette(&current, palette);
            child = current.next_sibling();
        }
    }
    apply_palette(bar.upcast_ref(), &palette);
    bar.set_visible(node.header.visible);
    bar
}
fn native_control(node: &Node, id: u32, suppress: Rc<Cell<bool>>) -> Result<Option<gtk::Widget>> {
    thread_local! {
        static BUTTON_RESET: gtk::CssProvider = {
            let reset = gtk::CssProvider::new();
            reset.load_from_data("button { background: transparent; box-shadow: none; }");
            reset
        };
    }
    let widget: gtk::Widget = match node.kind {
        Kind::Text | Kind::Button => {
            let label = gtk::Label::new(None);
            configure_label(&label, node);
            if node.kind == Kind::Text {
                label.set_yalign(0.);
                label.set_wrap_mode(gtk::pango::WrapMode::WordChar);
                label.upcast()
            } else {
                let button = gtk::Button::new();
                button.set_has_frame(false);
                // The enclosing Surface owns the background, border and state
                // styles. GTK's "flat" class still paints a theme hover fill.
                // Keep native focus outlines and input/accessibility behavior.
                BUTTON_RESET.with(|reset| {
                    #[allow(deprecated)]
                    button
                        .style_context()
                        .add_provider(reset, gtk::STYLE_PROVIDER_PRIORITY_APPLICATION);
                });
                button.set_child(Some(&label));
                button.connect_clicked(move |_| {
                    if !suppress.get() {
                        crate::events::enqueue(id);
                    }
                });
                button.upcast()
            }
        }
        Kind::TextField => {
            let field = gtk::Entry::new();
            field.set_has_frame(false);
            let blocked = suppress.clone();
            field.connect_changed(move |field| {
                if !blocked.get() {
                    crate::events::emit(id, 2, 0., field.text().to_string());
                }
            });
            field.upcast()
        }
        Kind::Switch => {
            let toggle = gtk::Switch::new();
            toggle.connect_active_notify(move |toggle| {
                if !suppress.get() {
                    crate::events::emit(
                        id,
                        3,
                        if toggle.is_active() { 1. } else { 0. },
                        String::new(),
                    );
                }
            });
            toggle.upcast()
        }
        Kind::RadioButton => {
            let radio = gtk::CheckButton::new();
            let label = gtk::Label::new(None);
            configure_label(&label, node);
            radio.set_child(Some(&label));
            radio.connect_toggled(move |radio| {
                if !suppress.get() && radio.is_active() {
                    crate::events::emit(id, 3, 1., String::new());
                }
            });
            radio.upcast()
        }
        Kind::Slider => {
            let scale = gtk::Scale::with_range(gtk::Orientation::Horizontal, 0., 1., 0.001);
            scale.set_draw_value(false);
            scale.connect_value_changed(move |scale| {
                if !suppress.get() {
                    crate::events::emit(id, 3, scale.value() as f32, String::new());
                }
            });
            scale.upcast()
        }
        Kind::Image | Kind::Icon => image(node)?.upcast(),
        Kind::NavigationHeader => header(node, id).upcast(),
        _ => return Ok(None),
    };
    if matches!(
        node.kind,
        Kind::Button | Kind::TextField | Kind::RadioButton | Kind::Switch | Kind::Slider
    ) {
        attach_hover(&widget, id);
    }
    if node.kind == Kind::Button {
        widget.connect_state_flags_changed(move |widget, old| {
            let pressed = widget.state_flags().contains(gtk::StateFlags::ACTIVE);
            if pressed != old.contains(gtk::StateFlags::ACTIVE) {
                crate::events::emit(id, 7, if pressed { 1. } else { 0. }, String::new());
            }
        });
    }
    if matches!(
        node.kind,
        Kind::Button | Kind::TextField | Kind::RadioButton | Kind::Switch | Kind::Slider
    ) {
        // Entry delegates focus to its internal text child. Track the whole control.
        let focus = gtk::EventControllerFocus::new();
        focus.connect_enter(move |_| crate::events::emit(id, 4, 1., String::new()));
        focus.connect_leave(move |_| crate::events::emit(id, 4, 0., String::new()));
        widget.add_controller(focus);
    }
    if node.kind == Kind::RadioButton {
        attach_radio_pressed(&widget, id);
    }
    configure_control(&widget, node);
    Ok(Some(widget))
}
// GTK can retain a pointer-inside state across sensitivity changes while a
// gesture is active. Reset on disable/unmap, and recover hover on real motion.
fn attach_hover(widget: &gtk::Widget, id: u32) {
    let hovered = Rc::new(Cell::new(false));
    let emit: Rc<dyn Fn(bool)> = Rc::new(move |value| {
        if hovered.replace(value) != value {
            crate::events::emit(id, 8, if value { 1. } else { 0. }, String::new());
        }
    });
    let motion = gtk::EventControllerMotion::new();
    let change = emit.clone();
    motion.connect_enter(move |controller, _, _| {
        if controller.widget().is_some_and(|w| w.is_sensitive()) {
            change(true);
        }
    });
    let change = emit.clone();
    motion.connect_motion(move |controller, _, _| {
        if controller.widget().is_some_and(|w| w.is_sensitive()) {
            change(true);
        }
    });
    let change = emit.clone();
    motion.connect_leave(move |_| change(false));
    let weak = motion.downgrade();
    let change = emit.clone();
    widget.connect_state_flags_changed(move |widget, _| {
        if !widget.is_sensitive() {
            if let Some(controller) = weak.upgrade() {
                controller.reset();
            }
            change(false);
        }
    });
    let weak = motion.downgrade();
    widget.connect_unmap(move |_| {
        if let Some(controller) = weak.upgrade() {
            controller.reset();
        }
        emit(false);
    });
    widget.add_controller(motion);
}

// GtkCheckButton does not expose pointer down as StateFlags::ACTIVE (GTK 4.8).
// Observe gestures without claiming them, so native selection/scroll cancellation remain intact.
fn attach_radio_pressed(widget: &gtk::Widget, id: u32) {
    let pressed = Rc::new(Cell::new(false));
    let emit: Rc<dyn Fn(bool)> = Rc::new(move |value| {
        if pressed.replace(value) != value {
            crate::events::emit(id, 7, if value { 1. } else { 0. }, String::new());
        }
    });
    let click = gtk::GestureClick::new();
    click.set_name(Some("dotnative-radio-press"));
    click.set_button(1);
    click.set_propagation_phase(gtk::PropagationPhase::Capture);
    let change = emit.clone();
    click.connect_pressed(move |gesture, _, _, _| {
        if gesture.widget().is_some_and(|w| w.is_sensitive()) {
            change(true);
        }
    });
    let change = emit.clone();
    click.connect_released(move |_, _, _, _| change(false));
    let change = emit.clone();
    click.connect_stopped(move |_| change(false));
    let change = emit.clone();
    click.connect_cancel(move |_, _| change(false));
    widget.add_controller(click);
    let keys = gtk::EventControllerKey::new();
    keys.set_name(Some("dotnative-radio-key-press"));
    keys.set_propagation_phase(gtk::PropagationPhase::Capture);
    let activation = |key| {
        matches!(
            key,
            gtk::gdk::Key::space
                | gtk::gdk::Key::KP_Space
                | gtk::gdk::Key::Return
                | gtk::gdk::Key::KP_Enter
        )
    };
    let change = emit.clone();
    keys.connect_key_pressed(move |keys, key, _, modifiers| {
        if activation(key)
            && !modifiers.intersects(
                gtk::gdk::ModifierType::CONTROL_MASK
                    | gtk::gdk::ModifierType::ALT_MASK
                    | gtk::gdk::ModifierType::SUPER_MASK,
            )
            && keys.widget().is_some_and(|w| w.is_sensitive())
        {
            change(true);
        }
        glib::Propagation::Proceed
    });
    let change = emit.clone();
    keys.connect_key_released(move |_, key, _, _| {
        if activation(key) {
            change(false);
        }
    });
    widget.add_controller(keys);
    let focus = gtk::EventControllerFocus::new();
    let change = emit.clone();
    focus.connect_leave(move |_| change(false));
    widget.add_controller(focus);
    let change = emit.clone();
    widget.connect_sensitive_notify(move |w| {
        if !w.is_sensitive() {
            change(false);
        }
    });
    widget.connect_unmap(move |_| emit(false));
}
fn configure_control(widget: &gtk::Widget, node: &Node) {
    widget.set_sensitive(node.enabled);
    if let Some(label) = widget.downcast_ref::<gtk::Label>() {
        configure_label(label, node);
    }
    if let Some(button) = widget.downcast_ref::<gtk::Button>() {
        if let Some(label) = button.child().and_downcast::<gtk::Label>() {
            configure_label(&label, node);
        }
    }
    if let Some(field) = widget.downcast_ref::<gtk::Entry>() {
        if field.text().as_str() != &*node.text {
            field.set_text(&node.text);
        }
        field.set_placeholder_text(Some(&node.control.placeholder));
        field.set_attributes(&attributes(node));
    }
    if let Some(toggle) = widget.downcast_ref::<gtk::Switch>() {
        toggle.set_active(node.control.value != 0.);
    }
    if let Some(radio) = widget.downcast_ref::<gtk::CheckButton>() {
        if let Some(label) = radio.child().and_downcast::<gtk::Label>() {
            configure_label(&label, node);
        }
        radio.set_active(node.control.value != 0.);
    }
    if let Some(scale) = widget.downcast_ref::<gtk::Scale>() {
        scale.set_range(node.control.minimum as f64, node.control.maximum as f64);
        let step = (node.control.maximum - node.control.minimum) as f64 / 1000.;
        scale.set_increments(step, step * 10.);
        scale.set_value(node.control.value as f64);
    }
}
#[derive(Default)]
struct NativeMeasurer {
    prototypes: HashMap<u8, gtk::Widget>,
}
impl Measure for NativeMeasurer {
    fn validate_style(&self, node: &Node) -> Result<()> {
        if node.kind == Kind::TextField
            && node.text_input != crate::protocol::TextInputOptions::default()
        {
            return Err(Error::Tree(
                "TextInputOptions currently requires iOS or Android",
            ));
        }
        if node.kind == Kind::DesktopNavigation {
            return Err(Error::Tree(
                "native desktop navigation requires macOS or Windows",
            ));
        }
        if node.kind == Kind::BottomNavigationBar {
            return Err(Error::Tree(
                "BottomNavigationBar is supported only on iOS and Android",
            ));
        }
        if node.typography.max_lines > 0 && matches!(node.typography.overflow, 1 | 2) {
            return Err(Error::Measurement(
                "GTK line limits require default or ellipsis overflow",
            ));
        }
        super::font_assets_linux::register(node)
    }
    fn measure(
        &mut self,
        node: &Node,
        known: Size<Option<f32>>,
        available: Size<AvailableSpace>,
    ) -> Result<Size<f32>> {
        if node.kind == Kind::PlatformView {
            return Ok(Size {
                width: known.width.unwrap_or(0.),
                height: known.height.unwrap_or(0.),
            });
        }
        // Detached prototypes are bounded by control kind; images/headers retain no cache.
        let reusable = matches!(
            node.kind,
            Kind::Text
                | Kind::Button
                | Kind::TextField
                | Kind::Switch
                | Kind::Slider
                | Kind::RadioButton
        );
        let widget = if reusable {
            if let Some(widget) = self.prototypes.get(&(node.kind as u8)) {
                configure_control(widget, node);
                widget.clone()
            } else {
                let widget = native_control(node, 0, Rc::new(Cell::new(true)))?
                    .ok_or(Error::Measurement("unsupported GTK leaf"))?;
                self.prototypes.insert(node.kind as u8, widget.clone());
                widget
            }
        } else {
            native_control(node, 0, Rc::new(Cell::new(true)))?
                .ok_or(Error::Measurement("unsupported GTK leaf"))?
        };
        let (minimum, natural, _, _) = widget.measure(gtk::Orientation::Horizontal, -1);
        let width = known
            .width
            .unwrap_or(match available.width {
                AvailableSpace::Definite(width) => (natural as f32).min(width).max(0.),
                AvailableSpace::MinContent => minimum as f32,
                AvailableSpace::MaxContent => natural as f32,
            })
            .max(0.);
        let (_, height, _, _) = widget.measure(gtk::Orientation::Vertical, width.ceil() as i32);
        Ok(Size {
            width,
            height: known.height.unwrap_or(height as f32).max(0.),
        })
    }
}
struct Entry {
    cells: crate::platform::cell_pool::CellPool<gtk::Fixed>,
    scroll_binding: Option<scroll::Binding>,
    surface: Surface,
    content: gtk::Fixed,
    scroll: Option<gtk::ScrolledWindow>,
    control: Option<gtk::Widget>,
    suppress: Rc<Cell<bool>>,
    radio_anchor: Option<gtk::CheckButton>,
    radio_group: Option<gtk::CheckButton>,
    applied: Option<Arc<Node>>,
    frame: Option<Frame>,
    parent: u32,
    hosted_type: String,
}
impl Entry {
    fn set_radio_group(&mut self, anchor: &gtk::CheckButton) {
        if self.radio_group.as_ref() == Some(anchor) {
            return;
        }
        let choice = self
            .control
            .as_ref()
            .unwrap()
            .downcast_ref::<gtk::CheckButton>()
            .unwrap();
        // GTK does not unlink an existing group when joining another. Rejoining
        // three siblings on every commit creates a cyclic list and hangs focus
        // traversal/activation. Detach first, and only regroup on membership changes.
        choice.set_group(None::<&gtk::CheckButton>);
        choice.set_group(Some(anchor));
        self.radio_group = Some(anchor.clone());
    }

    fn new(node: &Node, id: u32) -> Result<Self> {
        let surface: Surface = glib::Object::new();
        let suppress = Rc::new(Cell::new(true));
        let control = native_control(node, id, suppress.clone())?;
        if let Some(control) = &control {
            surface.put(control, 0., 0.);
        }
        let radio_anchor = if node.kind == Kind::RadioButton {
            let anchor = gtk::CheckButton::new();
            control
                .as_ref()
                .unwrap()
                .downcast_ref::<gtk::CheckButton>()
                .unwrap()
                .set_group(Some(&anchor));
            Some(anchor)
        } else {
            None
        };
        let (content, scroll) = if node.kind == Kind::ScrollView {
            let content = gtk::Fixed::new();
            let scroll = gtk::ScrolledWindow::new();
            scroll.set_overlay_scrolling(true);
            scroll.set_child(Some(&content));
            scroll.set_kinetic_scrolling(true);
            surface.put(&scroll, 0., 0.);
            (content, Some(scroll))
        } else {
            (surface.clone().upcast::<gtk::Fixed>(), None)
        };
        let scroll_binding = scroll
            .as_ref()
            .map(|view| scroll::Binding::new(view, &surface, id, suppress.clone()));
        Ok(Self {
            cells: Default::default(),
            scroll_binding,
            surface,
            content,
            scroll,
            control,
            suppress,
            radio_group: radio_anchor.clone(),
            radio_anchor,
            applied: None,
            frame: None,
            parent: 0,
            hosted_type: String::new(),
        })
    }
}
struct Registry {
    navigation: HashMap<u32, navigation::State>,
    host: gtk::Fixed,
    entries: HashMap<u32, Entry>,
}
thread_local! { static REGISTRY: RefCell<Option<Registry>> = const { RefCell::new(None) }; }
impl Registry {
    fn sync(&mut self, engine: &Engine) -> Result<()> {
        let hierarchy = super::hierarchy(engine)?;
        let nodes = engine.presentation_nodes();
        for entry in self.entries.values() {
            entry.suppress.set(true);
        }
        let previous = self
            .entries
            .iter()
            .filter_map(|(id, e)| e.applied.as_ref().map(|n| (*id, n.clone())))
            .collect();
        for (old, new) in crate::platform::row_recycling::plan(&previous, engine) {
            if let Some(mut entry) = self.entries.remove(&old) {
                if !entry.hosted_type.is_empty() {
                    if let Some(control) = &entry.control {
                        crate::platform_view::rebind(
                            old,
                            new,
                            &entry.hosted_type,
                            control.as_ptr().cast(),
                        );
                    }
                }
                entry.applied = None;
                entry.frame = None;
                self.entries.insert(new, entry);
                crate::events::rebind(old, new);
            }
        }
        crate::events::retain_bindings(|id| engine.node(id).is_some());
        for (id, _, _) in &hierarchy {
            if !self.entries.contains_key(id) {
                self.entries.insert(*id, Entry::new(&nodes[id], *id)?);
            }
        }
        let mut row_hosts = HashMap::new();
        for (_, list) in engine.nodes().filter(|(_, n)| n.virtual_list) {
            let document = list.children[0];
            let rows = &nodes[&document].children;
            let entry = self.entries.get_mut(&document).unwrap();
            entry.cells.sync(
                rows,
                || Ok(gtk::Fixed::new()),
                |cell| {
                    while let Some(child) = cell.first_child() {
                        cell.remove(&child);
                    }
                    if let Some(parent) = cell.parent().and_downcast::<gtk::Fixed>() {
                        parent.remove(cell);
                    }
                },
            )?;
            for id in rows {
                let cell = entry.cells.get(*id);
                let f = engine.frame(*id)?;
                if cell.parent().is_none() {
                    entry.content.put(cell, 0., 0.);
                }
                entry.content.move_(cell, f.x as f64, f.y as f64);
                cell.set_size_request(f.width.ceil() as i32, f.height.ceil() as i32);
                row_hosts.insert(*id, cell.clone());
            }
        }
        // Reparent survivors before removing obsolete ancestors. Keyed controls
        // keep their GTK object, focus, selection and scroll adjustments.
        for (id, parent, _) in &hierarchy {
            let destination = if let Some(cell) = row_hosts.get(id) {
                cell.clone()
            } else if *parent == 0 {
                self.host.clone()
            } else {
                self.entries[parent].content.clone()
            };
            let entry = self.entries.get_mut(id).unwrap();
            if entry.surface.parent().as_ref() != Some(destination.upcast_ref()) {
                if let Some(old) = entry.surface.parent().and_downcast::<gtk::Fixed>() {
                    old.remove(&entry.surface);
                }
                destination.put(&entry.surface, 0., 0.);
                entry.frame = None;
            }
            entry.parent = *parent;
        }
        self.entries.retain(|id, entry| {
            if nodes.contains_key(id) {
                true
            } else {
                if let Some(parent) = entry.surface.parent().and_downcast::<gtk::Fixed>() {
                    parent.remove(&entry.surface);
                }
                false
            }
        });
        for (id, node) in engine.nodes() {
            if node.kind.is_container() {
                let mut previous: Option<gtk::Widget> = None;
                for child in &node.paint_children {
                    let view: gtk::Widget = row_hosts
                        .get(child)
                        .map(|v| v.clone().upcast())
                        .unwrap_or_else(|| self.entries[child].surface.clone().upcast());
                    view.insert_after(&self.entries[&id].content, previous.as_ref());
                    previous = Some(view);
                }
                let anchor = node
                    .children
                    .iter()
                    .find_map(|child| self.entries[child].radio_anchor.clone());
                for child in &node.children {
                    let entry = self.entries.get_mut(child).unwrap();
                    if entry.radio_anchor.is_some() {
                        entry.set_radio_group(anchor.as_ref().unwrap());
                    }
                }
            }
        }
        for (id, parent, logical_frame) in &hierarchy {
            let mut local_frame = *logical_frame;
            if row_hosts.contains_key(id) {
                local_frame.x = 0.;
                local_frame.y = 0.;
            }
            let frame = &local_frame;
            let node = &nodes[id];
            let destination = if let Some(cell) = row_hosts.get(id) {
                cell.clone()
            } else if *parent == 0 {
                self.host.clone()
            } else {
                self.entries[parent].content.clone()
            };
            let entry = self.entries.get_mut(id).unwrap();
            let changed = entry
                .applied
                .as_ref()
                .is_none_or(|old| !Arc::ptr_eq(old, node));
            if changed {
                entry.surface.imp().style.set(node.style);
                entry.surface.imp().decoration.set(node.decoration);
                entry.surface.imp().visual.set(node.visual);
                *entry.surface.imp().gradient.borrow_mut() = node.gradient.clone();
                if node.kind != Kind::NavigationPage {
                    entry.surface.set_opacity(node.visual.opacity as f64);
                }
                entry.surface.set_visible(node.behavior.visibility == 0);
                entry.surface.set_overflow(if node.visual.clip {
                    gtk::Overflow::Hidden
                } else {
                    gtk::Overflow::Visible
                });
                if entry
                    .applied
                    .as_ref()
                    .is_none_or(|old| old.background_image != node.background_image)
                {
                    *entry.surface.imp().background_image.borrow_mut() =
                        if node.background_image.is_empty() {
                            None
                        } else {
                            image(&node.background_asset())?
                                .paintable()
                                .and_downcast::<gtk::gdk::Texture>()
                        };
                }
                entry.surface.imp().background_fit.set(node.background_fit);
                entry.surface.queue_draw();
                if node.kind == Kind::PlatformView
                    && entry.hosted_type.as_str() != node.platform_view.as_ref()
                {
                    if let Some(control) = entry.control.take() {
                        crate::platform_view::detach(
                            *id,
                            &entry.hosted_type,
                            control.as_ptr().cast(),
                        );
                        entry.surface.remove(&control);
                    }
                    entry.hosted_type = node.platform_view.to_string();
                    entry.control = crate::platform_view::create(*id, node.platform_view.as_ref())
                        .map(|pointer| unsafe {
                            use gtk::glib::translate::FromGlibPtrFull;
                            gtk::Widget::from_glib_full(pointer.as_ptr().cast())
                        });
                    if let Some(control) = &entry.control {
                        entry.surface.put(control, 0., 0.);
                    }
                }
                if node.kind == Kind::NavigationHeader
                    || (node.kind == Kind::Image
                        && entry
                            .applied
                            .as_ref()
                            .is_none_or(|old| old.image != node.image))
                {
                    if let Some(control) = entry.control.take() {
                        entry.surface.remove(&control);
                    }
                    entry.control = native_control(node, *id, entry.suppress.clone())?;
                    if let Some(control) = &entry.control {
                        entry.surface.put(control, 0., 0.);
                    }
                    entry.frame = None;
                }
                if let Some(control) = &entry.control {
                    configure_control(control, node);
                    if node.kind == Kind::Image {
                        let image = control.downcast_ref::<gtk::Image>().unwrap();
                        *entry.surface.imp().image.borrow_mut() =
                            image.paintable().and_downcast::<gtk::gdk::Texture>();
                        entry.surface.imp().image_fit.set(node.image_fit);
                        entry.surface.imp().image_tint.set(node.image_tint);
                        control.set_visible(false);
                    }
                }
            }
            if changed || entry.frame != Some(*frame) {
                let v = node.visual;
                let transform = gtk::gsk::Transform::new()
                    .translate(&gtk::graphene::Point::new(
                        frame.x + v.translate_x + frame.width / 2.,
                        frame.y + v.translate_y + frame.height / 2.,
                    ))
                    .rotate(v.rotation)
                    .scale(v.scale_x, v.scale_y)
                    .matrix(&gtk::graphene::Matrix::from_float(node.projection))
                    .translate(&gtk::graphene::Point::new(
                        -frame.width / 2.,
                        -frame.height / 2.,
                    ));
                if node.kind != Kind::NavigationPage || entry.frame.is_none() {
                    destination.set_child_transform(&entry.surface, Some(&transform));
                }
                entry
                    .surface
                    .set_size_request(frame.width.ceil() as i32, frame.height.ceil() as i32);
                if let Some(control) = &entry.control {
                    let p = node.padding();
                    entry.surface.move_(control, p[0] as f64, p[1] as f64);
                    control.set_size_request(
                        (frame.width - p[0] - p[2]).max(0.).ceil() as i32,
                        (frame.height - p[1] - p[3]).max(0.).ceil() as i32,
                    );
                }
                entry.frame = Some(*frame);
            }
            if let Some(scroll) = &entry.scroll {
                let horizontal = node.scroll_axis == 1;
                scroll.set_policy(
                    if horizontal {
                        gtk::PolicyType::Automatic
                    } else {
                        gtk::PolicyType::Never
                    },
                    if horizontal {
                        gtk::PolicyType::Never
                    } else {
                        gtk::PolicyType::Automatic
                    },
                );
                scroll.set_size_request(frame.width.ceil() as i32, frame.height.ceil() as i32);
                let extent = node.children.iter().try_fold(
                    (frame.width, frame.height),
                    |(w, h), child| {
                        let f = engine.frame(*child)?;
                        Ok::<_, Error>((w.max(f.x + f.width), h.max(f.y + f.height)))
                    },
                )?;
                entry.content.set_size_request(
                    (if horizontal { extent.0 } else { frame.width }).ceil() as i32,
                    (if horizontal { frame.height } else { extent.1 }).ceil() as i32,
                );
            }
            if let Some(binding) = &entry.scroll_binding {
                let extent = node.children.iter().try_fold(0f32, |max, child| {
                    let f = engine.frame(*child)?;
                    Ok::<_, Error>(max.max(if node.scroll_axis == 1 {
                        f.x + f.width
                    } else {
                        f.y + f.height
                    }))
                })?;
                binding.configure(
                    node,
                    extent as f64,
                    if node.scroll_axis == 1 {
                        frame.width
                    } else {
                        frame.height
                    } as f64,
                );
            }
            if node.kind == Kind::TextField
                && node.control.focus_revision > 0
                && entry
                    .applied
                    .as_ref()
                    .is_none_or(|old| old.control.focus_revision != node.control.focus_revision)
            {
                if node.control.focus_requested {
                    entry.control.as_ref().unwrap().grab_focus();
                } else if let Some(window) = entry.surface.root().and_downcast::<gtk::Window>() {
                    gtk::prelude::GtkWindowExt::set_focus(&window, None::<&gtk::Widget>);
                }
            }
            entry.applied = Some(node.clone());
        }
        if let Some(root) = nodes.get(&engine.root()) {
            if let Some(settings) = gtk::Settings::default() {
                let c = root.style.background;
                if c[3] == 255 {
                    settings.set_gtk_application_prefer_dark_theme(
                        c[0] as u32 + c[1] as u32 + (c[2] as u32) < 384,
                    );
                }
            }
        }
        for entry in self.entries.values() {
            entry.suppress.set(false);
            if let Some(binding) = &entry.scroll_binding {
                binding.report();
            }
        }
        self.sync_navigation(engine);
        Ok(())
    }
}
pub(crate) fn flush(bytes: &[u8]) -> Result<()> {
    if !gtk::is_initialized_main_thread() {
        return Err(Error::Tree("GTK requires its main thread"));
    }
    super::validate(bytes)?;
    REGISTRY.with(|registry| {
        let mut registry = registry
            .try_borrow_mut()
            .map_err(|_| Error::Tree("reentrant GTK sync"))?;
        let registry = registry
            .as_mut()
            .ok_or(Error::Tree("GTK host not initialized"))?;
        crate::ENGINE.with(|engine| {
            let mut engine = engine
                .try_borrow_mut()
                .map_err(|_| Error::Tree("reentrant engine"))?;
            let engine = engine.as_mut().ok_or(Error::Tree("missing engine"))?;
            engine.flush(bytes)?;
            engine.report_layouts()?;
            registry.sync(engine)
        })
    })
}
#[cfg(feature = "dev-host")]
fn schedule_frames(host: &gtk::Fixed, pump: impl FnMut(bool) + 'static) -> impl FnOnce() {
    let pump = std::rc::Rc::new(RefCell::new(pump));
    let last_frame = std::rc::Rc::new(Cell::new(std::time::Instant::now()));
    let frame_pump = pump.clone();
    let frame_time = last_frame.clone();
    let callback = host.add_tick_callback(move |_, _| {
        frame_time.set(std::time::Instant::now());
        (frame_pump.borrow_mut())(true);
        glib::ControlFlow::Continue
    });
    // GTK pauses the frame clock for invisible windows. Async work still needs a pump.
    let timer = glib::timeout_add_local(std::time::Duration::from_millis(100), move || {
        if last_frame.get().elapsed() >= std::time::Duration::from_millis(100) {
            (pump.borrow_mut())(false);
        }
        glib::ControlFlow::Continue
    });
    move || {
        callback.remove();
        timer.remove();
    }
}

#[cfg(feature = "dev-host")]
pub fn run_preview(smoke: bool) {
    use crate::dev_host;
    gtk::init().expect("GTK initialization failed: run inside an X11 or Wayland desktop session");
    let host = gtk::Fixed::new();
    host.set_hexpand(true);
    host.set_vexpand(true);
    let viewport = gtk::Overlay::new();
    viewport.add_overlay(&host);
    viewport.set_measure_overlay(&host, false);
    viewport.set_clip_overlay(&host, true);
    let window = gtk::Window::builder()
        .title("DotNative Preview — Linux")
        .default_width(640)
        .default_height(720)
        .child(&viewport)
        .build();
    crate::initialize(Box::new(NativeMeasurer::default())).expect("initialize engine");
    REGISTRY.with(|r| {
        *r.borrow_mut() = Some(Registry {
            navigation: HashMap::new(),
            host: host.clone(),
            entries: HashMap::new(),
        })
    });
    super::register_plugins(window.as_ptr().cast());
    let keys = gtk::EventControllerKey::new();
    keys.connect_key_pressed(|_, key, _, modifiers| {
        if key == gtk::gdk::Key::Left && modifiers.contains(gtk::gdk::ModifierType::ALT_MASK) {
            let header = REGISTRY.with(|r| {
                r.borrow().as_ref().and_then(|r| {
                    r.entries
                        .iter()
                        .find(|(_, e)| {
                            e.applied.as_ref().is_some_and(|n| {
                                n.kind == Kind::NavigationHeader
                                    && n.header.can_pop
                                    && e.surface.is_visible()
                            })
                        })
                        .map(|(id, _)| *id)
                })
            });
            if let Some(id) = header {
                crate::events::emit(id, 3, -1., String::new());
                return glib::Propagation::Stop;
            }
        }
        glib::Propagation::Proceed
    });
    window.add_controller(keys);
    let main_loop = glib::MainLoop::new(None, false);
    let closing = main_loop.clone();
    window.connect_close_request(move |_| {
        if !smoke {
            super::require(dev_host::dotnative_app_stop());
        }
        closing.quit();
        glib::Propagation::Proceed
    });
    window.present();
    let stop_frames: Box<dyn FnOnce()>;
    let smoke_frames = std::rc::Rc::new(Cell::new((0u32, 0u32)));
    if smoke {
        radio_smoke::run();
        virtual_list_smoke::run();
        let frames = smoke_frames.clone();
        stop_frames = Box::new(schedule_frames(&host, move |display| {
            let (visible, hidden) = frames.get();
            frames.set((visible + u32::from(display), hidden + u32::from(!display)));
        }));
        flush(&super::smoke_commands()).expect("desktop smoke flush");
        super::verify_unsupported_batch_is_atomic(flush);
        let mut themed_header =
            crate::ENGINE.with(|e| e.borrow().as_ref().unwrap().node(2).unwrap().clone());
        themed_header.text = "Theme header".into();
        themed_header.header.visible = true;
        themed_header.header.can_pop = true;
        themed_header.header.back_visible = true;
        themed_header.header.actions = vec!["Action".into()].into();
        for dark in [true, false, true, false] {
            let ink = if dark { 255 } else { 0 };
            themed_header.style.foreground = [ink, ink, ink, 255];
            let bar = header(&themed_header, 0);
            fn check_ink(widget: &gtk::Widget, ink: u8) {
                #[allow(deprecated)]
                let color = widget.style_context().color();
                assert!(
                    (color.red() - ink as f32 / 255.).abs() < 0.01,
                    "GTK header theme ink"
                );
                assert!(color.alpha() > 0.99, "GTK header invisible ink");
                let mut child = widget.first_child();
                while let Some(current) = child {
                    check_ink(&current, ink);
                    child = current.next_sibling();
                }
            }
            check_ink(bar.upcast_ref(), ink);
        }
        println!("PASS GTK explicit header theme colors cover title, actions and back icon");
        assert_eq!(
            viewport.measure(gtk::Orientation::Vertical, -1).0,
            0,
            "Absolute layout must not lock the window to its previous viewport height"
        );
        println!("PASS GTK viewport can shrink independently of absolute child frames");
        // Original variable fixture verifies registration and real Pango shaping.
        let mut font_node =
            crate::ENGINE.with(|e| e.borrow().as_ref().unwrap().node(2).unwrap().clone());
        font_node.font_asset = Some(
            crate::font_assets::FontAsset::new(
                "DotNativeTest",
                include_bytes!("../../../tests/fixtures/DotNativeTest.ttf"),
            )
            .unwrap(),
        );
        font_node.typography.family = std::sync::Arc::from("DotNativeTest");
        font_node.text = std::sync::Arc::from("AAAA");
        super::font_assets_linux::register(&font_node).expect("font registration");
        let text = gtk::Label::new(None);
        text.set_text(&font_node.text);
        text.set_attributes(Some(&attributes(&font_node)));
        let narrow = text.layout().pixel_size().0;
        font_node.font_axes = std::sync::Arc::from([(*b"wdth", 200.)]);
        text.set_attributes(Some(&attributes(&font_node)));
        let wide = text.layout().pixel_size().0;
        assert!(
            wide > narrow * 3 / 2,
            "variable font must change native measurement: {narrow} -> {wide}"
        );
        font_node.font_axes = std::sync::Arc::from([]);
        text.set_attributes(Some(&attributes(&font_node)));
        assert_eq!(text.layout().pixel_size().0, narrow);
        font_node.font_asset = Some(
            crate::font_assets::FontAsset::new(
                "DotNativeTest",
                include_bytes!("../../../tests/fixtures/DotNativeTest-Bold.ttf"),
            )
            .unwrap(),
        );
        super::font_assets_linux::register(&font_node).expect("bold face registration");
        font_node.typography.weight = 700;
        text.set_attributes(Some(&attributes(&font_node)));
        assert!(text.layout().pixel_size().0 > narrow * 14 / 10);
        font_node.typography.weight = 400;
        text.set_attributes(Some(&attributes(&font_node)));
        assert_eq!(text.layout().pixel_size().0, narrow);
        println!("PASS GTK font family faces, variable width shaping and reset");
        for styled in [true, false] {
            flush(&super::smoke_styles(styled)).expect("native style apply/reset");
            REGISTRY.with(|registry| {
                let registry = registry.borrow();
                assert_eq!(
                    registry.as_ref().unwrap().entries[&3]
                        .control
                        .as_ref()
                        .unwrap()
                        .is_sensitive(),
                    !styled
                );
                let entry = &registry.as_ref().unwrap().entries[&2];
                assert_eq!(
                    entry.surface.imp().decoration.get().corners.is_some(),
                    styled
                );
                assert_eq!(
                    entry.surface.imp().decoration.get().borders[0].is_some(),
                    styled
                );
                assert!((entry.surface.opacity() - if styled { 0.6 } else { 1. }).abs() < 0.01);
                let label = entry
                    .control
                    .as_ref()
                    .unwrap()
                    .downcast_ref::<gtk::Label>()
                    .unwrap();
                assert_eq!(
                    registry.as_ref().unwrap().entries[&1]
                        .surface
                        .imp()
                        .gradient
                        .borrow()
                        .is_some(),
                    styled
                );
                assert_eq!(
                    registry.as_ref().unwrap().entries[&1]
                        .content
                        .last_child()
                        .as_ref(),
                    Some(
                        registry.as_ref().unwrap().entries[&if styled { 2 } else { 3 }]
                            .surface
                            .upcast_ref()
                    )
                );
                assert_eq!(label.lines(), if styled { 2 } else { -1 });
                assert_eq!(label.xalign(), if styled { 0.5 } else { 0. });
            });
        }
        println!("PASS GTK native opacity, typography, gradient reset and identity");
        let text_height = || {
            REGISTRY.with(|r| {
                r.borrow().as_ref().unwrap().entries[&2]
                    .control
                    .as_ref()
                    .unwrap()
                    .measure(gtk::Orientation::Vertical, -1)
                    .1
            })
        };
        let plain_height = text_height();
        flush(&super::smoke_rich(true)).expect("rich text apply");
        assert!(
            text_height() > plain_height,
            "rich font participates in native measurement"
        );
        flush(&super::smoke_rich(false)).expect("rich text reset");
        assert_eq!(text_height(), plain_height);
        println!("PASS GTK rich text native measurement and reset");
        let button = REGISTRY.with(|r| {
            r.borrow().as_ref().unwrap().entries[&3]
                .control
                .clone()
                .unwrap()
                .downcast::<gtk::Button>()
                .unwrap()
        });
        let mut event = [0u8; 64];
        while crate::events::dotnative_poll_click() != 0 {}
        button.set_state_flags(gtk::StateFlags::ACTIVE, false);
        assert_eq!(
            unsafe { crate::events::dotnative_poll_event(event.as_mut_ptr(), event.len()) },
            16
        );
        assert_eq!(u32::from_le_bytes(event[4..8].try_into().unwrap()), 7);
        assert_eq!(f32::from_le_bytes(event[8..12].try_into().unwrap()), 1.);
        button.unset_state_flags(gtk::StateFlags::ACTIVE);
        assert_eq!(
            unsafe { crate::events::dotnative_poll_event(event.as_mut_ptr(), event.len()) },
            16
        );
        assert_eq!(f32::from_le_bytes(event[8..12].try_into().unwrap()), 0.);
        println!("PASS GTK native button pressed state events and disabled reset");
        let constrained = NativeMeasurer::default()
            .measure(
                &crate::ENGINE.with(|e| e.borrow().as_ref().unwrap().node(3).unwrap().clone()),
                Size {
                    width: Some(-12.),
                    height: Some(-4.),
                },
                Size {
                    width: AvailableSpace::Definite(0.),
                    height: AvailableSpace::Definite(0.),
                },
            )
            .expect("GTK measurement with exhausted content space");
        assert_eq!(
            constrained,
            Size {
                width: 0.,
                height: 0.
            }
        );
        println!("PASS GTK exhausted content space is clamped before native measurement");
        // A flat GtkButton still has hover/active theme fills unless explicitly
        // reset. Only the DotNative surface should paint those backgrounds.
        let button_node =
            crate::ENGINE.with(|e| e.borrow().as_ref().unwrap().node(3).unwrap().clone());
        let custom = native_control(&button_node, 0, Rc::new(Cell::new(true)))
            .unwrap()
            .unwrap();
        for state in [gtk::StateFlags::PRELIGHT, gtk::StateFlags::ACTIVE] {
            custom.set_state_flags(state, true);
            let snapshot = gtk::Snapshot::new();
            #[allow(deprecated)]
            snapshot.render_background(&custom.style_context(), 0., 0., 100., 40.);
            assert!(
                snapshot.to_node().is_none(),
                "GTK painted a second button background"
            );
        }
        println!("PASS GTK button hover/pressed background belongs to DotNative");
        button.emit_clicked();
        assert_eq!(crate::events::dotnative_poll_click(), 3);
        flush(&super::smoke_commands_update()).expect("desktop smoke update");
        REGISTRY.with(|r| {
            let r = r.borrow();
            let r = r.as_ref().unwrap();
            assert_eq!(
                r.entries[&2]
                    .control
                    .clone()
                    .unwrap()
                    .downcast::<gtk::Label>()
                    .unwrap()
                    .text(),
                "Count: 1"
            );
            assert!(!r.entries.contains_key(&3));
        });
        let renderer = gtk::gsk::CairoRenderer::new();
        renderer
            .realize(window.surface().as_ref())
            .expect("smoke renderer");
        let mut retained = None;
        for (index, (fit, tint)) in [
            (0u8, false),
            (1, false),
            (2, false),
            (3, false),
            (4, false),
            (0, true),
            (0, false),
        ]
        .into_iter()
        .enumerate()
        {
            flush(&super::smoke_image(index == 0, fit, tint)).expect("image style apply/reset");
            REGISTRY.with(|r| {
                let r = r.borrow();
                let entry = &r.as_ref().unwrap().entries[&4];
                assert_eq!(
                    r.as_ref().unwrap().entries[&1]
                        .surface
                        .imp()
                        .background_image
                        .borrow()
                        .is_some(),
                    tint
                );
                if let Some(old) = &retained {
                    assert_eq!(old, &entry.surface);
                } else {
                    retained = Some(entry.surface.clone());
                }
                assert!(entry.surface.imp().image.borrow().is_some());
                entry.surface.allocate(64, 64, -1, None);
                let snapshot = gtk::Snapshot::new();
                entry.surface.imp().snapshot(&snapshot);
                let node = snapshot.to_node().expect("image snapshot");
                let texture = renderer
                    .render_texture(&node, Some(&gtk::graphene::Rect::new(0., 0., 64., 64.)));
                let mut pixels = vec![0u8; 64 * 64 * 4];
                texture.download(&mut pixels, 64 * 4);
                let pixel = |x: usize, y: usize| {
                    u32::from_ne_bytes(
                        pixels[(y * 64 + x) * 4..(y * 64 + x) * 4 + 4]
                            .try_into()
                            .unwrap(),
                    )
                };
                let alpha = pixel(8, 8) >> 24;
                assert_eq!(alpha > 200, fit == 1 || fit == 2, "fit clipping {fit}");
                if fit == 0 {
                    let left = pixel(16, 32);
                    let right = pixel(48, 32);
                    if tint {
                        assert!(
                            (left >> 24).abs_diff(128) < 3 && ((left >> 8) & 255).abs_diff(128) < 3,
                            "opaque source tint: {left:x}"
                        );
                        assert!(
                            (right >> 24).abs_diff(64) < 3 && ((right >> 8) & 255).abs_diff(64) < 3,
                            "alpha mask tint: {right:x}"
                        );
                    } else {
                        assert_eq!(left, 0xffff0000);
                        assert!(
                            (right >> 24).abs_diff(128) < 3 && (right & 255).abs_diff(128) < 3,
                            "original colors: {right:x}"
                        );
                    }
                }
            });
        }
        // Render the actual native border painter, including transparent dash gaps.
        for style in 0..=2 {
            let decoration = crate::protocol::BoxDecoration {
                corners: Some([20., 0., 0., 0.]),
                borders: [Some(crate::protocol::BorderSide {
                    width: 4.,
                    color: [255, 0, 0, 255],
                    style,
                }); 4],
            };
            let geometry =
                crate::box_geometry::BoxGeometry::styles(decoration, 0., 0., [0; 4], 64., 64.);
            let snapshot = gtk::Snapshot::new();
            let bounds = gtk::graphene::Rect::new(0., 0., 64., 64.);
            draw_borders(&snapshot, &bounds, &geometry);
            let texture = renderer.render_texture(&snapshot.to_node().unwrap(), Some(&bounds));
            let mut pixels = vec![0u8; 64 * 64 * 4];
            texture.download(&mut pixels, 64 * 4);
            let alpha = |x: usize, y: usize| pixels[(y * 64 + x) * 4 + 3];
            assert_eq!(alpha(0, 0), 0, "rounded corner");
            assert_eq!(alpha(32, 32), 0, "hollow border");
            let painted = (24..60).filter(|&x| alpha(x, 2) > 200).count();
            if style == 0 {
                assert_eq!(painted, 36);
                assert!(alpha(62, 62) > 200);
            } else {
                assert!(painted > 0 && painted < 36, "dash gaps {style}: {painted}");
            }
        }
        println!("PASS GTK asymmetric rounded border, solid/dashed/dotted rendered pixels");
        renderer.unrealize();
        println!("PASS GTK image fit/tint rendered pixels, background reset, stable identity and sibling z-order");
        for kind in [
            Kind::RadioButton,
            Kind::Switch,
            Kind::Slider,
            Kind::TextField,
        ] {
            let mut node = crate::ENGINE
                .with(|engine| engine.borrow().as_ref().unwrap().node(2).unwrap().clone());
            node.kind = kind;
            let control = native_control(&node, 900, Rc::new(Cell::new(false)))
                .unwrap()
                .unwrap();
            host.put(&control, 0., 0.);
            gtk::prelude::GtkWindowExt::set_focus(&window, None::<&gtk::Widget>);
            while crate::events::dotnative_poll_click() != 0 {}
            assert!(control.grab_focus());
            super::expect_state(900, 4, 1.);
            gtk::prelude::GtkWindowExt::set_focus(&window, None::<&gtk::Widget>);
            super::expect_state(900, 4, 0.);
            let controllers = control.observe_controllers();
            let motion = (0..controllers.n_items())
                .find_map(|i| {
                    controllers
                        .item(i)
                        .unwrap()
                        .downcast::<gtk::EventControllerMotion>()
                        .ok()
                })
                .expect("hover controller");
            motion.emit_by_name::<()>("enter", &[&0f64, &0f64]);
            super::expect_state(900, 8, 1.);
            motion.emit_by_name::<()>("leave", &[]);
            super::expect_state(900, 8, 0.);
            if kind == Kind::RadioButton {
                let click = (0..controllers.n_items())
                    .find_map(|i| {
                        controllers
                            .item(i)
                            .unwrap()
                            .downcast::<gtk::GestureClick>()
                            .ok()
                            .filter(|c| c.name().as_deref() == Some("dotnative-radio-press"))
                    })
                    .unwrap();
                for end in ["released", "stopped", "cancel"] {
                    click.emit_by_name::<()>("pressed", &[&1i32, &0f64, &0f64]);
                    super::expect_state(900, 7, 1.);
                    if end == "released" {
                        click.emit_by_name::<()>(end, &[&1i32, &0f64, &0f64]);
                    } else if end == "cancel" {
                        click.emit_by_name::<()>(end, &[&None::<gtk::gdk::EventSequence>]);
                    } else {
                        click.emit_by_name::<()>(end, &[]);
                    }
                    super::expect_state(900, 7, 0.);
                }
                click.emit_by_name::<()>("pressed", &[&1i32, &0f64, &0f64]);
                super::expect_state(900, 7, 1.);
                control.set_sensitive(false);
                super::expect_state(900, 7, 0.);
                control.set_sensitive(true);
                let keys = (0..controllers.n_items())
                    .find_map(|i| {
                        controllers
                            .item(i)
                            .unwrap()
                            .downcast::<gtk::EventControllerKey>()
                            .ok()
                            .filter(|c| c.name().as_deref() == Some("dotnative-radio-key-press"))
                    })
                    .unwrap();
                let key = gtk::gdk::Key::space;
                let modifiers = gtk::gdk::ModifierType::empty();
                let consumed = keys.emit_by_name::<bool>("key-pressed", &[&key, &0u32, &modifiers]);
                assert!(
                    !consumed,
                    "state observer must not consume native activation"
                );
                super::expect_state(900, 7, 1.);
                keys.emit_by_name::<()>("key-released", &[&key, &0u32, &modifiers]);
                super::expect_state(900, 7, 0.);
            }
            host.remove(&control);
        }
        println!("PASS GTK control focus/hover and radio pressed/reset events");

        let end = main_loop.clone();
        glib::timeout_add_local_once(std::time::Duration::from_millis(150), move || end.quit());
    } else {
        // Keep the OS preference independent of the per-app GTK dark override.
        let system_theme = theme::SystemTheme::new();
        let mut previous_theme = 0;
        let mut previous = (0, 0);
        let mounted_host = host.clone();
        stop_frames = Box::new(schedule_frames(&host, move |_| {
            let host = &mounted_host;
            let theme = system_theme.current();
            if theme != previous_theme {
                super::require(dev_host::dotnative_app_system_theme(theme));
                previous_theme = theme;
            }
            if let Some(settings) = gtk::Settings::default() {
                let dpi = settings.gtk_xft_dpi();
                let scale = if dpi > 0 {
                    (dpi as f32 / (96. * 1024.)).clamp(0.5, 5.)
                } else {
                    1.
                };
                let contrast = settings
                    .gtk_theme_name()
                    .is_some_and(|name| name.to_ascii_lowercase().contains("highcontrast"));
                super::require(dev_host::dotnative_app_accessibility(
                    (!settings.is_gtk_enable_animations()) as i32,
                    scale,
                    contrast as i32,
                ));
            }
            let size = (host.width(), host.height());
            if size.0 > 0 && size.1 > 0 && size != previous {
                if previous == (0, 0) {
                    super::require(dev_host::dotnative_app_start(size.0 as f32, size.1 as f32));
                } else {
                    super::require(dev_host::dotnative_app_resize(size.0 as f32, size.1 as f32));
                }
                previous = size;
            }
            if previous != (0, 0) {
                super::require(dev_host::dotnative_app_tick());
            }
        }));
    }
    main_loop.run();
    if smoke {
        assert!(smoke_frames.get().0 > 0, "GTK frame clock did not tick");
        window.set_visible(false);
        let end = main_loop.clone();
        glib::timeout_add_local_once(std::time::Duration::from_millis(350), move || end.quit());
        main_loop.run();
        assert!(
            smoke_frames.get().1 > 0,
            "hidden window stopped async progress"
        );
    }
    stop_frames();
    if smoke {
        let stopped = smoke_frames.get();
        let end = main_loop.clone();
        glib::timeout_add_local_once(std::time::Duration::from_millis(150), move || end.quit());
        main_loop.run();
        assert_eq!(smoke_frames.get(), stopped, "callbacks survived shutdown");
        println!("PASS GTK display frames, hidden-window progress and callback cleanup");
    }
    window.destroy();
    REGISTRY.with(|r| r.borrow_mut().take());
    if smoke {
        println!("PASS GTK native create, measure, layout, click, update and delete");
    }
}
