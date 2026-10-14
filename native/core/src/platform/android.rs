//! Android JNI adapter. GlobalRefs own native Views; no local JNI reference
//! escapes a call frame. All entry points are invoked by the Activity main thread.
use crate::{AvailableSpace, Engine, Error, Kind, Measure, Node, Result, Size};
use jni::{
    objects::{GlobalRef, JClass, JFloatArray, JObject, JValue},
    sys::{jfloat, jint},
    JNIEnv, JavaVM,
};
use std::{
    cell::RefCell,
    collections::{HashMap, HashSet},
    sync::Arc,
};
fn err(_: jni::errors::Error) -> Error {
    Error::Measurement("Android JNI call failed")
}
struct NativeMeasurer {
    vm: Arc<JavaVM>,
    host: GlobalRef,
}
fn font_axes(node: &Node) -> String {
    node.font_axes
        .iter()
        .map(|(tag, value)| format!("'{}' {value}", std::str::from_utf8(tag).unwrap()))
        .collect::<Vec<_>>()
        .join(",")
}
fn text_run_data(n: &Node) -> Vec<u8> {
    let mut bytes = (n.text_runs.len() as u32).to_le_bytes().to_vec();
    for run in n.text_runs.iter() {
        let styled = n.run_style(run);
        bytes.extend((run.text.encode_utf16().count() as u32).to_le_bytes());
        bytes.extend(styled.style.font_size.to_le_bytes());
        bytes.extend(color(styled.style.foreground).to_le_bytes());
        bytes.extend((styled.typography.weight as i32).to_le_bytes());
        bytes.push(styled.typography.italic as u8);
        bytes.push(styled.typography.decoration);
        bytes.extend((styled.typography.family.len() as u32).to_le_bytes());
        bytes.extend(styled.typography.family.as_bytes());
    }
    bytes
}
impl Measure for NativeMeasurer {
    fn validate_style(&self, node: &Node) -> Result<()> {
        if node.kind == Kind::DesktopNavigation {
            return Err(Error::Tree(
                "native desktop navigation requires macOS or Windows",
            ));
        }
        crate::font_assets::ensure(node, |asset| {
            let mut env = self.vm.get_env().map_err(err)?;
            env.with_local_frame(8, |env| -> Result<Box<dyn std::any::Any>> {
                let family = env.new_string(&*asset.family)?;
                let data = env.byte_array_from_slice(&asset.data)?;
                env.call_method(
                    self.host.as_obj(),
                    "registerFont",
                    "(Ljava/lang/String;[B)V",
                    &[JValue::Object(&family), JValue::Object(&data)],
                )?;
                Ok(Box::new(()))
            })
        })
    }

    fn measure(
        &mut self,
        n: &Node,
        known: Size<Option<f32>>,
        available: Size<AvailableSpace>,
    ) -> Result<Size<f32>> {
        let mut env = self.vm.get_env().map_err(err)?;
        env.with_local_frame(16, |env| -> Result<_> {
            let text = env.new_string(&*n.text).map_err(err)?;
            let width = known.width.unwrap_or(match available.width {
                AvailableSpace::Definite(v) => v.max(0.),
                AvailableSpace::MinContent => 0.,
                AvailableSpace::MaxContent => -1.,
            });
            let image = env.byte_array_from_slice(&n.image)?;
            let family = env.new_string(&*n.typography.family)?;
            let runs = env.byte_array_from_slice(&text_run_data(n))?;
            let axes = env.new_string(font_axes(n))?;
            let result = env
                .call_method(
                    self.host.as_obj(),
                    "measure",
                    "(ILjava/lang/String;FF[BLjava/lang/String;IZIIIFFI[BLjava/lang/String;IIIII)[F",
                    &[
                        JValue::Int(native_kind(n.kind)),
                        JValue::Object(&text),
                        JValue::Float(n.style.font_size),
                        JValue::Float(width),
                        JValue::Object(&image),
                        JValue::Object(&family),
                        JValue::Int(n.typography.weight as i32),
                        JValue::Bool(n.typography.italic as u8),
                        JValue::Int(n.text_alignment() as i32),
                        JValue::Int(n.typography.max_lines),
                        JValue::Int(n.typography.overflow as i32),
                        JValue::Float(n.typography.letter_spacing),
                        JValue::Float(n.typography.line_height),
                        JValue::Int(n.typography.decoration as i32),
                        JValue::Object(&runs),
                        JValue::Object(&axes),
                        JValue::Int(n.text_input.keyboard as i32),
                        JValue::Int(n.text_input.action as i32),
                        JValue::Int(n.text_input.capitalization as i32),
                        JValue::Int(n.text_input.autofill as i32),
                        JValue::Int(n.text_input.flags as i32),
                    ],
                )
                .map_err(err)?
                .l()
                .map_err(err)?;
            let mut values = [0f32; 2];
            env.get_float_array_region(JFloatArray::from(result), 0, &mut values)
                .map_err(err)?;
            Ok(Size {
                width: known.width.unwrap_or(values[0]),
                height: known.height.unwrap_or(values[1]),
            })
        })
    }
}
// with_local_frame needs the JNI error conversion for frame allocation failures.
impl From<jni::errors::Error> for Error {
    fn from(e: jni::errors::Error) -> Self {
        err(e)
    }
}
struct Registry {
    vm: Arc<JavaVM>,
    host: GlobalRef,
    views: HashMap<u32, GlobalRef>,
    mounted: HashMap<u32, String>,
    applied: im::HashMap<u32, std::sync::Arc<Node>>,
    frames: HashMap<u32, crate::engine::Frame>,
    root: u32,
}
// Keep the Java ABI explicit; Rust enum discriminants are not wire IDs.
fn native_kind(kind: Kind) -> i32 {
    kind.wire_id()
}

fn color(v: [u8; 4]) -> i32 {
    u32::from_be_bytes([v[3], v[0], v[1], v[2]]) as i32
}
impl Registry {
    fn sync(&mut self, e: &Engine) -> Result<()> {
        let frames: Vec<_> = e
            .nodes()
            .map(|(id, _)| e.frame(id).map(|f| (id, f)))
            .collect::<Result<_>>()?;
        let virtual_documents: HashSet<u32> = e
            .nodes()
            .filter(|(_, n)| n.virtual_list)
            .map(|(_, n)| n.children[0])
            .collect();
        let virtual_parents: HashSet<u32> = e
            .nodes()
            .filter(|(_, n)| n.virtual_list)
            .map(|(id, _)| id)
            .chain(virtual_documents.iter().copied())
            .collect();
        let mut rebound = HashSet::new();
        for (old, new) in super::row_recycling::plan(&self.applied, e) {
            if let Some(view) = self.views.remove(&old) {
                self.views.insert(new, view);
                if let Some(name) = self.mounted.remove(&old) {
                    self.mounted.insert(new, name);
                    rebound.insert(new);
                }
                crate::events::rebind(old, new);
            }
        }
        crate::events::retain_bindings(|id| e.node(id).is_some());
        let mut env = self.vm.get_env()?;
        let deleted: Vec<_> = self
            .views
            .keys()
            .filter(|id| e.node(**id).is_none())
            .copied()
            .collect();
        for id in deleted {
            self.frames.remove(&id);
            self.mounted.remove(&id);
            let v = self.views.remove(&id).unwrap();
            env.call_method(v.as_obj(), "detachPlatformView", "()V", &[])?;
            env.call_method(
                self.host.as_obj(),
                "remove",
                "(Landroid/view/View;)V",
                &[JValue::Object(v.as_obj())],
            )?;
        }
        for (id, n) in e.nodes() {
            let mount = n.kind == Kind::PlatformView
                && (self.mounted.get(&id).map(String::as_str) != Some(n.platform_view.as_ref())
                    || rebound.contains(&id));
            env.with_local_frame(16, |env| -> Result<()> {
                if !self.views.contains_key(&id) {
                    let v = env
                        .call_method(
                            self.host.as_obj(),
                            "create",
                            "(IIZ)Landroid/view/View;",
                            &[
                                JValue::Int(id as i32),
                                JValue::Int(native_kind(n.kind)),
                                JValue::Bool(n.virtual_list as u8),
                            ],
                        )?
                        .l()?;
                    self.views.insert(id, env.new_global_ref(v)?);
                }
                if self.applied.get(&id).is_none_or(|o| {
                    o.text != n.text
                        || o.style != n.style
                        || o.extended_layout != n.extended_layout
                        || o.padding() != n.padding()
                }) {
                    let text = env.new_string(&*n.text)?;
                    let s = n.style;
                    env.call_method(
                        self.host.as_obj(),
                        "configure",
                        "(Landroid/view/View;Ljava/lang/String;FFFFIFIF)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&text),
                            JValue::Float(n.padding()[0]),
                            JValue::Float(n.padding()[1]),
                            JValue::Float(n.padding()[2]),
                            JValue::Float(n.padding()[3]),
                            JValue::Int(color(s.background)),
                            JValue::Float(s.radius),
                            JValue::Int(color(s.foreground)),
                            JValue::Float(s.font_size),
                        ],
                    )?;
                }
                if self
                    .applied
                    .get(&id)
                    .is_none_or(|old| old.gradient != n.gradient)
                {
                    let g = n.gradient.as_ref();
                    let count = g.map_or(0, |g| g.stops.len());
                    let offsets = env.new_float_array(count as i32)?;
                    let colors = env.new_int_array(count as i32)?;
                    if let Some(g) = g {
                        env.set_float_array_region(
                            &offsets,
                            0,
                            &g.stops.iter().map(|s| s.0).collect::<Vec<_>>(),
                        )?;
                        env.set_int_array_region(
                            &colors,
                            0,
                            &g.stops.iter().map(|s| color(s.1)).collect::<Vec<_>>(),
                        )?;
                    }
                    env.call_method(
                        self.host.as_obj(),
                        "gradient",
                        "(Landroid/view/View;[F[IIFFFF)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&offsets),
                            JValue::Object(&colors),
                            JValue::Int(g.map_or(0, |g| g.kind as i32)),
                            JValue::Float(g.map_or(0., |g| g.start[0])),
                            JValue::Float(g.map_or(0., |g| g.start[1])),
                            JValue::Float(g.map_or(0., |g| g.end[0])),
                            JValue::Float(g.map_or(0., |g| g.end[1])),
                        ],
                    )?;
                }
                if self
                    .applied
                    .get(&id)
                    .is_none_or(|old| old.behavior.visibility != n.behavior.visibility)
                {
                    env.call_method(
                        self.views[&id].as_obj(),
                        "setVisibility",
                        "(I)V",
                        &[JValue::Int(if n.behavior.visibility == 0 {
                            0
                        } else if n.behavior.visibility == 1 {
                            4
                        } else {
                            8
                        })],
                    )?;
                }
                if self.applied.get(&id).is_none_or(|old| {
                    old.font_revision != n.font_revision
                        || old.typography != n.typography
                        || old.resolved_flow != n.resolved_flow
                        || old.text != n.text
                        || old.style != n.style
                }) {
                    let t = &n.typography;
                    let family = env.new_string(&*t.family)?;
                    env.call_method(
                        self.host.as_obj(),
                        "typography",
                        "(Landroid/view/View;Ljava/lang/String;IZIIIFFI)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&family),
                            JValue::Int(t.weight as i32),
                            JValue::Bool(t.italic as u8),
                            JValue::Int(n.text_alignment() as i32),
                            JValue::Int(t.max_lines),
                            JValue::Int(t.overflow as i32),
                            JValue::Float(t.letter_spacing),
                            JValue::Float(t.line_height),
                            JValue::Int(t.decoration as i32),
                        ],
                    )?;
                }
                if self
                    .applied
                    .get(&id)
                    .is_none_or(|old| old.projection != n.projection)
                {
                    let array = env.new_float_array(16)?;
                    env.set_float_array_region(&array, 0, &n.projection)?;
                    env.call_method(
                        self.host.as_obj(),
                        "projection",
                        "(Landroid/view/View;[F)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&array),
                        ],
                    )?;
                }
                if n.kind != Kind::NavigationPage
                    && self
                        .applied
                        .get(&id)
                        .is_none_or(|old| old.visual != n.visual || old.style != n.style)
                {
                    let v = n.visual;
                    let values = [
                        v.opacity,
                        v.border_width,
                        v.shadow_blur,
                        v.shadow_x,
                        v.shadow_y,
                        v.translate_x,
                        v.translate_y,
                        v.scale_x,
                        v.scale_y,
                        v.rotation,
                    ];
                    let array = env.new_float_array(values.len() as i32)?;
                    env.set_float_array_region(&array, 0, &values)?;
                    env.call_method(
                        self.host.as_obj(),
                        "visual",
                        "(Landroid/view/View;[FIIZ)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&array),
                            JValue::Int(color(v.border_color)),
                            JValue::Int(color(v.shadow_color)),
                            JValue::Bool(v.clip as u8),
                        ],
                    )?;
                }
                if matches!(
                    n.kind,
                    Kind::TextField | Kind::Switch | Kind::Slider | Kind::RadioButton
                ) && self
                    .applied
                    .get(&id)
                    .is_none_or(|old| old.control != n.control)
                {
                    let placeholder = env.new_string(&*n.control.placeholder)?;
                    env.call_method(
                        self.host.as_obj(),
                        "control",
                        "(Landroid/view/View;FFFIZLjava/lang/String;)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Float(n.control.value),
                            JValue::Float(n.control.minimum),
                            JValue::Float(n.control.maximum),
                            JValue::Int(n.control.focus_revision as i32),
                            JValue::Bool(n.control.focus_requested as u8),
                            JValue::Object(&placeholder),
                        ],
                    )?;
                }
                if n.kind == Kind::TextField
                    && self
                        .applied
                        .get(&id)
                        .is_none_or(|old| old.text_input != n.text_input)
                {
                    env.call_method(
                        self.host.as_obj(),
                        "textInput",
                        "(Landroid/view/View;IIIII)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Int(n.text_input.keyboard as i32),
                            JValue::Int(n.text_input.action as i32),
                            JValue::Int(n.text_input.capitalization as i32),
                            JValue::Int(n.text_input.autofill as i32),
                            JValue::Int(n.text_input.flags as i32),
                        ],
                    )?;
                }
                if n.kind == Kind::TextField
                    && self
                        .applied
                        .get(&id)
                        .is_none_or(|old| old.text_input != n.text_input || old.style != n.style)
                {
                    let mut hint = n.style.foreground;
                    hint[3] = 153;
                    env.call_method(
                        self.host.as_obj(),
                        "hintColor",
                        "(Landroid/view/View;I)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Int(color(n.text_input.placeholder_color.unwrap_or(hint))),
                        ],
                    )?;
                }
                if n.kind == Kind::NavigationHeader
                    && self.applied.get(&id).is_none_or(|old| {
                        old.header != n.header || old.text != n.text || old.style != n.style
                    })
                {
                    let labels = env.new_object_array(
                        n.header.actions.len() as i32,
                        "java/lang/String",
                        JObject::null(),
                    )?;
                    for (index, label) in n.header.actions.iter().enumerate() {
                        let text = env.new_string(&**label)?;
                        env.set_object_array_element(&labels, index as i32, &text)?;
                        env.delete_local_ref(text)?;
                    }
                    let title = env.new_string(&*n.text)?;
                    env.call_method(
                        self.host.as_obj(),
                        "header",
                        "(Landroid/view/View;Ljava/lang/String;ZZI[Ljava/lang/String;I)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&title),
                            JValue::Bool(n.header.back_visible as u8),
                            JValue::Bool(n.header.can_pop as u8),
                            JValue::Int(n.header.leading as i32),
                            JValue::Object(&labels),
                            JValue::Int(color(n.style.foreground)),
                        ],
                    )?;
                }
                if n.kind == Kind::BottomNavigationBar
                    && self
                        .applied
                        .get(&id)
                        .is_none_or(|old| old.bottom_navigation != n.bottom_navigation)
                {
                    let labels = env.new_object_array(
                        n.bottom_navigation.items.len() as i32,
                        "java/lang/String",
                        JObject::null(),
                    )?;
                    let icons = env.new_int_array(n.bottom_navigation.items.len() as i32)?;
                    let values: Vec<i32> = n
                        .bottom_navigation
                        .items
                        .iter()
                        .map(|(icon, _)| *icon as i32)
                        .collect();
                    env.set_int_array_region(&icons, 0, &values)?;
                    for (index, (_, label)) in n.bottom_navigation.items.iter().enumerate() {
                        let text = env.new_string(&**label)?;
                        env.set_object_array_element(&labels, index as i32, &text)?;
                        env.delete_local_ref(text)?;
                    }
                    env.call_method(
                        self.host.as_obj(),
                        "bottomNavigation",
                        "(Landroid/view/View;I[Ljava/lang/String;[I)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Int(n.bottom_navigation.selected as i32),
                            JValue::Object(&labels),
                            JValue::Object(&icons),
                        ],
                    )?;
                }
                if n.kind == Kind::ScrollView
                    && self
                        .applied
                        .get(&id)
                        .is_none_or(|old| old.scroll_position != n.scroll_position)
                {
                    env.call_method(
                        self.host.as_obj(),
                        "scrollPosition",
                        "(Landroid/view/View;IFZ)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Int(n.scroll_position.0 as i32),
                            JValue::Float(n.scroll_position.1),
                            JValue::Bool(n.scroll_position.2 as u8),
                        ],
                    )?;
                }
                if n.kind == Kind::ScrollView
                    && self.applied.get(&id).is_none_or(|old| {
                        old.contain_scroll != n.contain_scroll || old.scroll_axis != n.scroll_axis
                    })
                {
                    env.call_method(
                        self.host.as_obj(),
                        "scrollPolicy",
                        "(Landroid/view/View;ZI)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Bool(n.contain_scroll as u8),
                            JValue::Int(n.scroll_axis as i32),
                        ],
                    )?;
                }
                if self.applied.get(&id).is_none_or(|old| {
                    old.background_image != n.background_image
                        || old.background_fit != n.background_fit
                }) && (n.kind.is_container())
                {
                    let bytes = env.byte_array_from_slice(&n.background_image)?;
                    env.call_method(
                        self.host.as_obj(),
                        "backgroundImage",
                        "(Landroid/view/View;[BI)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&bytes),
                            JValue::Int(n.background_fit as i32),
                        ],
                    )?;
                }
                if self
                    .applied
                    .get(&id)
                    .is_none_or(|old| old.enabled != n.enabled)
                {
                    env.call_method(
                        self.host.as_obj(),
                        "enabled",
                        "(Landroid/view/View;Z)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Bool(n.enabled as u8),
                        ],
                    )?;
                }
                if matches!(
                    n.kind,
                    Kind::Text | Kind::Button | Kind::TextField | Kind::RadioButton
                ) && self.applied.get(&id).is_none_or(|old| {
                    old.font_axes != n.font_axes
                        || old.font_revision != n.font_revision
                        || old.typography != n.typography
                        || old.style != n.style
                        || old.text != n.text
                }) {
                    let axes = env.new_string(font_axes(n))?;
                    env.call_method(
                        self.host.as_obj(),
                        "fontVariations",
                        "(Landroid/view/View;Ljava/lang/String;)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&axes),
                        ],
                    )?;
                }
                if n.kind == Kind::Text
                    && self.applied.get(&id).is_none_or(|old| {
                        old.font_axes != n.font_axes
                            || old.text_runs != n.text_runs
                            || old.text != n.text
                            || old.style != n.style
                            || old.font_revision != n.font_revision
                            || old.typography != n.typography
                    })
                {
                    let runs = env.byte_array_from_slice(&text_run_data(n))?;
                    env.call_method(
                        self.host.as_obj(),
                        "richText",
                        "(Landroid/view/View;[B)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&runs),
                        ],
                    )?;
                }
                if self.applied.get(&id).is_none_or(|old| {
                    old.decoration != n.decoration
                        || old.style.radius != n.style.radius
                        || old.visual.border_width != n.visual.border_width
                        || old.visual.border_color != n.visual.border_color
                }) {
                    let radii = n.decoration.corners.unwrap_or([n.style.radius; 4]);
                    let borders = n.decoration.borders.map(|b| {
                        b.unwrap_or(crate::protocol::BorderSide {
                            width: n.visual.border_width,
                            color: n.visual.border_color,
                            style: 0,
                        })
                    });
                    let values = env.new_float_array(8)?;
                    let mut data = [0.; 8];
                    data[..4].copy_from_slice(&radii);
                    data[4..].copy_from_slice(&borders.map(|b| b.width));
                    env.set_float_array_region(&values, 0, &data)?;
                    let colors = env.new_int_array(4)?;
                    env.set_int_array_region(&colors, 0, &borders.map(|b| color(b.color)))?;
                    let styles = env.new_int_array(4)?;
                    env.set_int_array_region(&styles, 0, &borders.map(|b| b.style as i32))?;
                    env.call_method(
                        self.host.as_obj(),
                        "decoration",
                        "(Landroid/view/View;[F[I[I)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&values),
                            JValue::Object(&colors),
                            JValue::Object(&styles),
                        ],
                    )?;
                }
                if n.kind == Kind::Image
                    && self.applied.get(&id).is_none_or(|old| old.image != n.image)
                {
                    let bytes = env.byte_array_from_slice(&n.image)?;
                    env.call_method(
                        self.host.as_obj(),
                        "image",
                        "(Landroid/view/View;[B)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&bytes),
                        ],
                    )?;
                }
                if mount {
                    let name = env.new_string(&*n.platform_view)?;
                    env.call_method(
                        self.views[&id].as_obj(),
                        "mountPlatformView",
                        "(ILjava/lang/String;)V",
                        &[JValue::Int(id as i32), JValue::Object(&name)],
                    )?;
                }
                Ok(())
            })?;
            if mount {
                self.mounted.insert(id, n.platform_view.to_string());
            }
        }
        for (id, node) in e.nodes() {
            if node.kind == Kind::Image
                && self
                    .applied
                    .get(&id)
                    .is_none_or(|old| old.image_fit != node.image_fit)
            {
                env.call_method(
                    self.host.as_obj(),
                    "imageFit",
                    "(Landroid/view/View;I)V",
                    &[
                        JValue::Object(self.views[&id].as_obj()),
                        JValue::Int(node.image_fit as i32),
                    ],
                )?;
            }
        }
        for (id, node) in e.nodes() {
            if node.kind == Kind::Image
                && self
                    .applied
                    .get(&id)
                    .is_none_or(|old| old.image_tint != node.image_tint)
            {
                let color = node.image_tint.unwrap_or([0; 4]);
                env.call_method(
                    self.host.as_obj(),
                    "imageTint",
                    "(Landroid/view/View;ZI)V",
                    &[
                        JValue::Object(self.views[&id].as_obj()),
                        JValue::Bool(node.image_tint.is_some() as u8),
                        JValue::Int(i32::from_be_bytes([color[3], color[0], color[1], color[2]])),
                    ],
                )?;
            }
        }
        for (id, node) in e.nodes() {
            if virtual_parents.contains(&id) || !node.kind.is_container() {
                continue;
            }
            if self
                .applied
                .get(&id)
                .is_none_or(|old| old.children != node.children)
            {
                env.with_local_frame(8, |env| -> Result<()> {
                    let children = env.new_object_array(
                        node.children.len() as i32,
                        "android/view/View",
                        JObject::null(),
                    )?;
                    for (index, child) in node.children.iter().enumerate() {
                        env.set_object_array_element(
                            &children,
                            index as i32,
                            self.views[child].as_obj(),
                        )?;
                    }
                    env.call_method(
                        self.host.as_obj(),
                        "children",
                        "(Landroid/view/View;[Landroid/view/View;)V",
                        &[
                            JValue::Object(self.views[&id].as_obj()),
                            JValue::Object(&children),
                        ],
                    )?;
                    Ok(())
                })?;
            }
        }
        for (id, node) in e.nodes() {
            if virtual_parents.contains(&id) {
                continue;
            }
            if self.applied.get(&id).is_none_or(|old| {
                old.paint_children != node.paint_children || old.children != node.children
            }) {
                for child in &node.paint_children {
                    env.call_method(self.views[child].as_obj(), "bringToFront", "()V", &[])?;
                }
            }
        }
        if let Some(root) = e.node(e.root()) {
            if self.root != e.root()
                || self
                    .applied
                    .get(&e.root())
                    .is_none_or(|old| old.style.background != root.style.background)
            {
                // Transparent roots leave the runner's initial OS surface intact.
                // NavigationHost provides the effective application's opaque palette.
                if root.style.background[3] == 255 {
                    env.call_method(
                        self.host.as_obj(),
                        "chrome",
                        "(I)V",
                        &[JValue::Int(color(root.style.background))],
                    )?;
                }
            }
        }
        if self.root != e.root() {
            let null = JObject::null();
            let root = self
                .views
                .get(&e.root())
                .map(|v| v.as_obj())
                .unwrap_or(&null);
            env.call_method(
                self.host.as_obj(),
                "root",
                "(Landroid/view/View;)V",
                &[JValue::Object(root)],
            )?;
        }
        for (id, f) in frames {
            if self.frames.get(&id) == Some(&f) {
                continue;
            }
            env.call_method(
                self.host.as_obj(),
                "frame",
                "(Landroid/view/View;FFFF)V",
                &[
                    JValue::Object(self.views[&id].as_obj()),
                    JValue::Float(f.x),
                    JValue::Float(f.y),
                    JValue::Float(f.width),
                    JValue::Float(f.height),
                ],
            )?;
            self.frames.insert(id, f);
        }
        for (id, node) in e.nodes().filter(|(_, n)| n.virtual_list) {
            let document = e
                .node(node.children[0])
                .expect("validated virtual document");
            env.with_local_frame(8, |env| -> Result<()> {
                let rows = env.new_object_array(
                    document.children.len() as i32,
                    "android/view/View",
                    JObject::null(),
                )?;
                let mut positions = Vec::with_capacity(document.children.len() + 1);
                for (index, child) in document.children.iter().enumerate() {
                    env.set_object_array_element(&rows, index as i32, self.views[child].as_obj())?;
                    positions.push(e.frame(*child)?.y);
                }
                positions.push(e.frame(node.children[0])?.height);
                let offsets = env.new_float_array(positions.len() as i32)?;
                env.set_float_array_region(&offsets, 0, &positions)?;
                env.call_method(
                    self.host.as_obj(),
                    "virtualList",
                    "(Landroid/view/View;[Landroid/view/View;[F)V",
                    &[
                        JValue::Object(self.views[&id].as_obj()),
                        JValue::Object(&rows),
                        JValue::Object(&offsets),
                    ],
                )?;
                Ok(())
            })?;
        }
        for (id, node) in e.nodes().filter(|(_, n)| n.kind == Kind::Navigation) {
            env.with_local_frame(8, |env| -> Result<()> {
                let pages = env.new_object_array(
                    node.children.len() as i32,
                    "android/view/View",
                    JObject::null(),
                )?;
                for (index, child) in node.children.iter().enumerate() {
                    env.set_object_array_element(&pages, index as i32, self.views[child].as_obj())?;
                }
                env.call_method(
                    self.host.as_obj(),
                    "navigation",
                    "(Landroid/view/View;[Landroid/view/View;IIFI)V",
                    &[
                        JValue::Object(self.views[&id].as_obj()),
                        JValue::Object(&pages),
                        JValue::Int(node.control.value as i32),
                        JValue::Int(node.navigation_transition as i32),
                        JValue::Float(node.navigation_duration),
                        JValue::Int(node.navigation_easing as i32),
                    ],
                )?;
                Ok(())
            })?;
        }
        self.root = e.root();
        self.applied = e.presentation_nodes();
        Ok(())
    }
}
thread_local! {static REGISTRY:RefCell<Option<Registry>>=const{RefCell::new(None)};}
pub(crate) fn flush(bytes: &[u8]) -> Result<()> {
    REGISTRY.with(|r| {
        let mut r = r
            .try_borrow_mut()
            .map_err(|_| Error::Tree("reentrant Android sync"))?;
        let r = r
            .as_mut()
            .ok_or(Error::Tree("initialize Android before flush"))?;
        crate::ENGINE.with(|e| {
            let mut e = e
                .try_borrow_mut()
                .map_err(|_| Error::Tree("reentrant engine"))?;
            let e = e.as_mut().ok_or(Error::Tree("missing engine"))?;
            e.flush(bytes)?;
            e.report_layouts()?;
            r.sync(e)
        })
    })
}
extern "C" {
    fn dotnative_app_start(w: f32, h: f32) -> i32;
    fn dotnative_app_resize(w: f32, h: f32) -> i32;
    fn dotnative_app_insets(top: f32, bottom: f32, keyboard: f32) -> i32;
    fn dotnative_app_tick() -> i32;
    fn dotnative_app_stop() -> i32;
    fn dotnative_app_activity(activity: i32) -> i32;
    fn dotnative_app_system_theme(theme: i32) -> i32;
    fn dotnative_app_accessibility(motion: i32, scale: f32, contrast: i32) -> i32;
}
// extern C never permits unwinding. Release builds also use panic=abort.
#[no_mangle]
pub extern "system" fn Java_com_dotnative_runtime_NativeHost_start(
    mut env: JNIEnv,
    _class: JClass,
    host: JObject,
    w: jfloat,
    h: jfloat,
) -> jint {
    let result = (|| -> Result<()> {
        let current = env
            .call_static_method(
                "android/os/Looper",
                "myLooper",
                "()Landroid/os/Looper;",
                &[],
            )?
            .l()?;
        let main = env
            .call_static_method(
                "android/os/Looper",
                "getMainLooper",
                "()Landroid/os/Looper;",
                &[],
            )?
            .l()?;
        if !env.is_same_object(current, main)? {
            return Err(Error::Tree("Android requires main looper"));
        }
        let vm = Arc::new(env.get_java_vm()?);
        let host = env.new_global_ref(host)?;
        // Activity recreation is allowed only after stop has released the old session.
        crate::initialize(Box::new(NativeMeasurer {
            vm: vm.clone(),
            host: host.clone(),
        }))?;
        REGISTRY.with(|r| {
            *r.borrow_mut() = Some(Registry {
                vm,
                host,
                views: HashMap::new(),
                mounted: HashMap::new(),
                applied: im::HashMap::new(),
                frames: HashMap::new(),
                root: 0,
            })
        });
        Ok(())
    })();
    match result {
        // Android can lay out the replacement Activity before the previous
        // Activity's onDestroy releases the process-wide native session.
        Err(Error::Tree("session already initialized")) => return 1,
        Err(_) => return -1,
        Ok(()) => {}
    }
    unsafe { dotnative_app_start(w, h) }
}
#[no_mangle]
pub extern "system" fn Java_com_dotnative_runtime_NativeHost_resize(
    _env: JNIEnv,
    _class: JClass,
    w: jfloat,
    h: jfloat,
) -> jint {
    unsafe { dotnative_app_resize(w, h) }
}
#[no_mangle]
pub extern "system" fn Java_com_dotnative_runtime_NativeHost_windowInsets(
    _env: JNIEnv,
    _class: JClass,
    top: jfloat,
    bottom: jfloat,
    keyboard: jfloat,
) -> jint {
    unsafe { dotnative_app_insets(top, bottom, keyboard) }
}
#[no_mangle]
pub extern "system" fn Java_com_dotnative_runtime_NativeHost_tick(
    _env: JNIEnv,
    _class: JClass,
) -> jint {
    unsafe { dotnative_app_tick() }
}
#[no_mangle]
pub extern "system" fn Java_com_dotnative_runtime_NativeHost_click(
    _env: JNIEnv,
    _class: JClass,
    id: jint,
) {
    crate::events::enqueue(id as u32);
}
#[no_mangle]
pub extern "system" fn Java_com_dotnative_runtime_NativeHost_stop(
    _env: JNIEnv,
    _class: JClass,
) -> jint {
    let result = unsafe { dotnative_app_stop() };
    if result == 0 {
        crate::plugins::detach();
        PLUGIN_BRIDGE.with(|slot| slot.borrow_mut().take());
        crate::events::clear();
        REGISTRY.with(|r| r.borrow_mut().take());
        crate::ENGINE.with(|e| e.borrow_mut().take());
    }
    result
}

#[no_mangle]
pub extern "system" fn Java_com_dotnative_runtime_NativeHost_event(
    mut env: JNIEnv,
    _class: JClass,
    id: jint,
    kind: jint,
    value: jfloat,
    text: jni::objects::JString,
) {
    let text: String = match env.get_string(&text) {
        Ok(s) => s.into(),
        Err(_) => return,
    };
    crate::events::emit(id as u32, kind as u32, value, text);
}

#[no_mangle]
pub extern "system" fn Java_com_dotnative_runtime_NativeHost_activity(
    _env: JNIEnv,
    _class: JClass,
    activity: jint,
) -> jint {
    unsafe { dotnative_app_activity(activity) }
}

#[no_mangle]
pub extern "system" fn Java_com_dotnative_runtime_NativeHost_systemTheme(
    _env: JNIEnv,
    _class: JClass,
    theme: jint,
) -> jint {
    unsafe { dotnative_app_system_theme(theme) }
}

// One JNI bridge for all plugins. Native SDKs copy borrowed request bytes before
// returning; no per-plugin JNI functions are required.
thread_local! {
    static PLUGIN_BRIDGE: RefCell<Option<(Arc<JavaVM>, GlobalRef)>> = const { RefCell::new(None) };
}
unsafe extern "C" fn dispatch_plugin(bytes: *const u8, len: usize) {
    let bridge = PLUGIN_BRIDGE.with(|slot| slot.borrow().clone());
    if let Some((vm, class)) = bridge {
        if let Ok(mut env) = vm.attach_current_thread() {
            let result = env.with_local_frame(8, |env| -> jni::errors::Result<()> {
                let data =
                    env.byte_array_from_slice(unsafe { std::slice::from_raw_parts(bytes, len) })?;
                let class: &JClass = class.as_obj().into();
                env.call_static_method(class, "dispatch", "([B)V", &[JValue::Object(&data)])?;
                Ok(())
            });
            if result.is_err() {
                let _ = env.exception_clear();
            }
        }
    }
}
#[no_mangle]
pub extern "system" fn Java_com_dotnative_plugins_NativeChannels_registerNative(
    env: JNIEnv,
    class: JClass,
    id: jint,
) -> jint {
    let bridge = (|| -> jni::errors::Result<_> {
        Ok((Arc::new(env.get_java_vm()?), env.new_global_ref(class)?))
    })();
    let Ok(bridge) = bridge else {
        return -1;
    };
    PLUGIN_BRIDGE.with(|slot| *slot.borrow_mut() = Some(bridge));
    crate::plugins::dotnative_register_channel(id as u32, Some(dispatch_plugin))
}
#[no_mangle]
pub extern "system" fn Java_com_dotnative_plugins_NativeChannels_nativeReply(
    env: JNIEnv,
    _class: JClass,
    bytes: jni::objects::JByteArray,
) -> jint {
    let Ok(length) = env.get_array_length(&bytes) else {
        return -1;
    };
    if !(13..=1048588).contains(&length) {
        return -1;
    }
    let Ok(bytes) = env.convert_byte_array(bytes) else {
        return -1;
    };
    unsafe { crate::plugins::dotnative_plugin_reply(bytes.as_ptr(), bytes.len()) }
}

#[no_mangle]
pub extern "system" fn Java_com_dotnative_runtime_NativeHost_accessibility(
    _env: JNIEnv,
    _class: JClass,
    motion: jint,
    scale: f32,
    contrast: jint,
) -> jint {
    unsafe { dotnative_app_accessibility(motion, scale, contrast) }
}
