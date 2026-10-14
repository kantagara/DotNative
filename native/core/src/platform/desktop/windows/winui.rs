//! Whole-window WinUI 3 presentation. Win32 is used only to host the XAML island.
use super::*;
use windows_sys::Win32::System::LibraryLoader::{
    GetProcAddress, LoadLibraryExW, LOAD_LIBRARY_SEARCH_DEFAULT_DIRS,
    LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR,
};
type Emit = extern "C" fn(u32, u32, f32, *const u8, i32);
type Create = unsafe extern "C" fn(HWND, Emit) -> isize;
type Update = unsafe extern "C" fn(isize, *const u8, i32, i32, i32) -> i32;
type MeasureNode = unsafe extern "C" fn(*const u8, i32, f32, f32, *mut f32) -> i32;
type Destroy = unsafe extern "C" fn(isize);
type Smoke = unsafe extern "C" fn(isize, u32) -> i32;
#[derive(Clone, Copy)]
struct Api {
    initialize: unsafe extern "C" fn() -> i32,
    create: Create,
    update: Update,
    measure: MeasureNode,
    destroy: Destroy,
    smoke: Smoke,
}
static API: std::sync::OnceLock<Option<Api>> = std::sync::OnceLock::new();
fn api() -> Result<Api> {
    API.get_or_init(|| unsafe {
        let path = std::env::current_exe()
            .ok()?
            .parent()?
            .join("DotNative.WinUI.dll");
        let module = LoadLibraryExW(
            wide(&path.to_string_lossy()).as_ptr(),
            null_mut(),
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS,
        );
        if module.is_null() {
            return None;
        }
        Some(Api {
            initialize: std::mem::transmute::<
                unsafe extern "system" fn() -> isize,
                unsafe extern "C" fn() -> i32,
            >(GetProcAddress(
                module,
                c"dotnative_winui_initialize".as_ptr().cast(),
            )?),
            create: std::mem::transmute::<unsafe extern "system" fn() -> isize, Create>(
                GetProcAddress(module, c"dotnative_winui_create".as_ptr().cast())?,
            ),
            update: std::mem::transmute::<unsafe extern "system" fn() -> isize, Update>(
                GetProcAddress(module, c"dotnative_winui_update".as_ptr().cast())?,
            ),
            measure: std::mem::transmute::<unsafe extern "system" fn() -> isize, MeasureNode>(
                GetProcAddress(module, c"dotnative_winui_measure".as_ptr().cast())?,
            ),
            destroy: std::mem::transmute::<unsafe extern "system" fn() -> isize, Destroy>(
                GetProcAddress(module, c"dotnative_winui_destroy".as_ptr().cast())?,
            ),
            smoke: std::mem::transmute::<unsafe extern "system" fn() -> isize, Smoke>(
                GetProcAddress(module, c"dotnative_winui_smoke".as_ptr().cast())?,
            ),
        })
    })
    .ok_or(Error::Tree(
        "WinUI 3 runtime unavailable; publish DotNative.WinUI beside the host",
    ))
}
pub(super) fn initialize() -> Result<()> {
    if unsafe { (api()?.initialize)() } != 0 {
        return Err(Error::Tree("WinUI runtime initialization failed"));
    }
    Ok(())
}
extern "C" fn emit(id: u32, kind: u32, value: f32, text: *const u8, length: i32) {
    if !(0..=1024 * 1024).contains(&length) || (length > 0 && text.is_null()) {
        return;
    }
    let text = if length == 0 {
        String::new()
    } else {
        String::from_utf8_lossy(unsafe { std::slice::from_raw_parts(text, length as usize) })
            .into_owned()
    };
    crate::events::emit(id, kind, value, text);
    wake_ui();
}
fn u(out: &mut Vec<u8>, value: u32) {
    out.extend(value.to_le_bytes());
}
fn f(out: &mut Vec<u8>, value: f32) {
    out.extend(value.to_le_bytes());
}
fn bytes(out: &mut Vec<u8>, value: &[u8]) {
    u(out, value.len() as u32);
    out.extend(value);
}
pub(super) fn node_packet(id: u32, node: &Node, frame: Frame) -> Vec<u8> {
    let mut out = Vec::new();
    u(&mut out, id);
    u(&mut out, node.kind.wire_id() as u32);
    for v in [frame.x, frame.y, frame.width, frame.height] {
        f(&mut out, v);
    }
    for v in node.padding() {
        f(&mut out, v);
    }
    out.extend(node.style.background);
    out.extend(node.style.foreground);
    for v in [
        node.style.radius,
        node.style.font_size,
        node.visual.opacity,
        node.visual.border_width,
    ] {
        f(&mut out, v);
    }
    out.extend(node.visual.border_color);
    for v in [
        node.visual.translate_x,
        node.visual.translate_y,
        node.visual.scale_x,
        node.visual.scale_y,
        node.visual.rotation,
    ] {
        f(&mut out, v);
    }
    for v in node.projection {
        f(&mut out, v);
    }
    for v in [
        node.enabled as u32,
        node.visual.clip as u32,
        node.resolved_flow as u32,
        node.behavior.visibility as u32,
        node.z_index as u32,
    ] {
        u(&mut out, v);
    }
    bytes(&mut out, node.text.as_bytes());
    bytes(&mut out, node.typography.family.as_bytes());
    for v in [
        node.typography.weight as u32,
        node.typography.italic as u32,
        node.text_alignment() as u32,
        node.typography.max_lines as u32,
        node.typography.overflow as u32,
        node.typography.decoration as u32,
    ] {
        u(&mut out, v);
    }
    f(&mut out, node.typography.letter_spacing);
    f(&mut out, node.typography.line_height);
    for v in [
        node.control.value,
        node.control.minimum,
        node.control.maximum,
    ] {
        f(&mut out, v);
    }
    u(&mut out, node.control.focus_revision);
    u(&mut out, node.control.focus_requested as u32);
    bytes(&mut out, node.control.placeholder.as_bytes());
    for v in [
        node.text_input.keyboard,
        node.text_input.action,
        node.text_input.capitalization,
        node.text_input.autofill,
        node.text_input.flags,
    ] {
        u(&mut out, v as u32);
    }
    u(&mut out, node.virtual_list as u32);
    u(&mut out, node.scroll_axis as u32);
    u(&mut out, node.scroll_position.0);
    f(&mut out, node.scroll_position.1);
    u(&mut out, node.scroll_position.2 as u32);
    bytes(&mut out, &node.image);
    u(&mut out, node.image_fit as u32);
    u(&mut out, node.children.len() as u32);
    for id in &node.children {
        u(&mut out, *id);
    }
    for v in [
        node.header.visible as u32,
        node.header.back_visible as u32,
        node.header.can_pop as u32,
        node.header.leading as u32,
    ] {
        u(&mut out, v);
    }
    u(&mut out, node.header.actions.len() as u32);
    for action in &node.header.actions {
        bytes(&mut out, action.as_bytes());
    }
    let nav = &node.desktop_navigation;
    for v in [nav.theme, nav.selected as u32, nav.open as u32] {
        u(&mut out, v);
    }
    f(&mut out, nav.width);
    bytes(&mut out, nav.title.as_bytes());
    u(&mut out, nav.items.len() as u32);
    for (label, icon) in &nav.items {
        bytes(&mut out, label.as_bytes());
        bytes(&mut out, icon.as_bytes());
    }
    u(&mut out, node.text_runs.len() as u32);
    for run in node.text_runs.iter() {
        bytes(&mut out, run.text.as_bytes());
        u(&mut out, run.mask as u32);
        f(&mut out, run.font_size);
        out.extend(run.foreground);
        u(&mut out, run.weight as u32);
        u(&mut out, run.italic as u32);
        bytes(&mut out, run.family.as_bytes());
        u(&mut out, run.decoration as u32);
    }
    u(&mut out, node.navigation_transition as u32);
    f(&mut out, node.navigation_duration);
    u(&mut out, node.navigation_easing as u32);
    u(
        &mut out,
        node.gradient.as_ref().map_or(0, |g| g.kind) as u32,
    );
    if let Some(g) = &node.gradient {
        for v in g.start.into_iter().chain(g.end) {
            f(&mut out, v);
        }
        u(&mut out, g.stops.len() as u32);
        for (offset, color) in &g.stops {
            f(&mut out, *offset);
            out.extend(color);
        }
    }
    bytes(&mut out, &node.background_image);
    u(&mut out, node.background_fit as u32);
    for radius in node.decoration.corners.unwrap_or([node.style.radius; 4]) {
        f(&mut out, radius);
    }
    bytes(
        &mut out,
        border_svg(node, frame.width, frame.height).as_bytes(),
    );
    bytes(&mut out, node.platform_view.as_bytes());
    out
}

fn border_svg(node: &Node, width: f32, height: f32) -> String {
    use crate::box_geometry::{BoxGeometry, Path, Segment};
    use std::fmt::Write;
    if node.decoration == Default::default() || width <= 0. || height <= 0. {
        return String::new();
    }
    fn path(commands: &Path) -> String {
        let mut s = String::new();
        for command in commands {
            match command {
                Segment::Move(x, y) => {
                    let _ = write!(s, "M{x} {y} ");
                }
                Segment::Line(x, y) => {
                    let _ = write!(s, "L{x} {y} ");
                }
                Segment::Curve([a, b, c, d, e, f]) => {
                    let _ = write!(s, "C{a} {b} {c} {d} {e} {f} ");
                }
                Segment::Close => s.push_str("Z "),
            }
        }
        s
    }
    let g = BoxGeometry::new(node, width, height);
    let ring = path(&g.ring);
    let center = path(&g.center);
    let mut svg = format!(
        r#"<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}"><defs><clipPath id="ring"><path d="{ring}" clip-rule="evenodd"/></clipPath>"#
    );
    for (i, wedge) in g.wedges.iter().enumerate() {
        let _ = write!(
            svg,
            r#"<clipPath id="w{i}"><path d="{}"/></clipPath>"#,
            path(wedge)
        );
    }
    svg.push_str(r#"</defs><g clip-path="url(#ring)">"#);
    for (i, b) in g.borders.iter().enumerate() {
        if b.width <= 0. || b.color[3] == 0 {
            continue;
        }
        let [r, g, b_, a] = b.color;
        let opacity = a as f32 / 255.;
        let _ = write!(svg, r#"<g clip-path="url(#w{i})">"#);
        if b.style == 0 {
            let _ = write!(
                svg,
                r#"<path d="{ring}" fill="rgb({r},{g},{b_})" fill-opacity="{opacity}" fill-rule="evenodd"/>"#
            );
        } else {
            let dash = if b.style == 1 {
                b.width * 3.
            } else {
                b.width * 0.001
            };
            let gap = b.width * 2.;
            let cap = if b.style == 2 { "round" } else { "butt" };
            let _ = write!(
                svg,
                r#"<path d="{center}" fill="none" stroke="rgb({r},{g},{b_})" stroke-opacity="{opacity}" stroke-width="{}" stroke-dasharray="{dash} {gap}" stroke-linecap="{cap}"/>"#,
                b.width
            );
        }
        svg.push_str("</g>");
    }
    svg.push_str("</g></svg>");
    svg
}
pub(super) struct Renderer {
    handle: isize,
    api: Api,
    applied: im::HashMap<u32, std::sync::Arc<Node>>,
    frames: HashMap<u32, Frame>,
}
impl Renderer {
    pub(super) fn new(host: HWND) -> Result<Self> {
        let api = api()?;
        let handle = unsafe { (api.create)(host, emit) };
        if handle == 0 {
            return Err(Error::Tree("WinUI initialization failed"));
        }
        Ok(Self {
            handle,
            api,
            applied: im::HashMap::new(),
            frames: HashMap::new(),
        })
    }
    pub(super) fn sync(&mut self, engine: &Engine, width: i32, height: i32) -> Result<()> {
        let next = engine.presentation_nodes();
        let mut frames = HashMap::with_capacity(next.len());
        let mut changed = Vec::new();
        for (&id, node) in &next {
            let frame = engine.frame(id)?;
            if self
                .applied
                .get(&id)
                .is_none_or(|old| !std::sync::Arc::ptr_eq(old, node))
                || self.frames.get(&id) != Some(&frame)
            {
                changed.push(node_packet(id, node, frame));
            }
            frames.insert(id, frame);
        }
        let deleted: Vec<_> = self
            .applied
            .keys()
            .filter(|id| !next.contains_key(id))
            .copied()
            .collect();
        let mut packet = Vec::new();
        u(&mut packet, 3);
        u(&mut packet, engine.root());
        u(&mut packet, deleted.len() as u32);
        for id in deleted {
            u(&mut packet, id);
        }
        u(&mut packet, changed.len() as u32);
        for node in changed {
            bytes(&mut packet, &node);
        }
        if unsafe {
            (self.api.update)(
                self.handle,
                packet.as_ptr(),
                packet.len() as i32,
                width,
                height,
            )
        } != 0
        {
            return Err(Error::Tree("WinUI presentation failed"));
        }
        crate::events::retain_bindings(|id| engine.node(id).is_some());
        self.applied = next;
        self.frames = frames;
        Ok(())
    }
    pub(super) fn smoke(&self, id: u32) {
        assert_eq!(
            unsafe { (self.api.smoke)(self.handle, id) },
            0,
            "WinUI native control smoke"
        );
    }
}
impl Drop for Renderer {
    fn drop(&mut self) {
        unsafe { (self.api.destroy)(self.handle) };
    }
}
#[derive(Default)]
pub(super) struct NativeMeasurer {
    cache: HashMap<Vec<u8>, Size<f32>>,
    order: std::collections::VecDeque<Vec<u8>>,
}
impl Measure for NativeMeasurer {
    fn validate_style(&self, node: &Node) -> Result<()> {
        if node.kind == Kind::BottomNavigationBar {
            return Err(Error::Tree("BottomNavigationBar is mobile-only"));
        }
        if node.font_asset.is_some()
            || !node.font_axes.is_empty()
            || node.gradient.as_ref().is_some_and(|g| g.kind != 1)
            || node.image_tint.is_some()
            || node.visual.shadow_color[3] != 0
        {
            return Err(Error::Tree("WinUI renderer does not yet support embedded/variable fonts, non-linear gradients, tint or shadows"));
        }
        Ok(())
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
        let constraint = |known: Option<f32>, space: AvailableSpace| {
            known
                .unwrap_or(match space {
                    AvailableSpace::Definite(v) => v,
                    AvailableSpace::MinContent => 0.,
                    AvailableSpace::MaxContent => 1_000_000.,
                })
                .max(0.)
        };
        let packet = node_packet(
            0,
            node,
            Frame {
                x: 0.,
                y: 0.,
                width: 0.,
                height: 0.,
            },
        );
        let width = constraint(known.width, available.width);
        let height = constraint(known.height, available.height);
        // Bound cached keys as well as entry count; do not retain large images or text.
        let key = if packet.len() <= 8192 && node.image.is_empty() {
            let mut key = packet.clone();
            f(&mut key, width);
            f(&mut key, height);
            u(&mut key, known.width.is_some() as u32);
            u(&mut key, known.height.is_some() as u32);
            Some(key)
        } else {
            None
        };
        if let Some(value) = key.as_ref().and_then(|key| self.cache.get(key)) {
            return Ok(*value);
        }
        let mut result = [0f32; 2];
        if unsafe {
            (api()?.measure)(
                packet.as_ptr(),
                packet.len() as i32,
                width,
                height,
                result.as_mut_ptr(),
            )
        } != 0
        {
            return Err(Error::Measurement("WinUI native measurement failed"));
        }
        let size = Size {
            width: known.width.unwrap_or(result[0]),
            height: known.height.unwrap_or(result[1]),
        };
        if let Some(key) = key {
            if self.order.len() == 256 {
                if let Some(old) = self.order.pop_front() {
                    self.cache.remove(&old);
                }
            }
            self.order.push_back(key.clone());
            self.cache.insert(key, size);
        }
        Ok(size)
    }
}
