use crate::{
    protocol::{self, Command, Kind, Style as UiStyle},
    Error, Result,
};
use std::{
    collections::{HashMap, HashSet},
    sync::Arc,
};
use taffy::prelude::*;
use taffy::{Overflow, Point};

const MAX_NODES: usize = 100_000;
const MAX_DEPTH: usize = 256;
#[derive(Clone, Debug)]
pub struct Node {
    pub kind: Kind,
    pub text: Arc<str>,
    pub style: UiStyle,
    pub layout: protocol::LayoutStyle,
    pub advanced_layout: protocol::AdvancedLayout,
    pub resolved_flow: u8,
    pub extended_layout: protocol::ExtendedLayout,
    pub visual: protocol::VisualStyle,
    pub projection: [f32; 16],
    pub background_image: Arc<[u8]>,
    pub background_fit: u8,
    pub image_tint: Option<[u8; 4]>,
    pub image_fit: u8,
    pub font_revision: u64,
    pub font_asset: Option<crate::font_assets::FontAsset>,
    pub font_axes: Arc<[([u8; 4], f32)]>,
    pub text_runs: Arc<[protocol::TextRun]>,
    pub enabled: bool,
    pub decoration: protocol::BoxDecoration,
    pub z_index: i32,
    pub paint_children: Vec<u32>,
    pub gradient: Option<protocol::Gradient>,
    pub typography: protocol::TypographyStyle,
    pub behavior: protocol::LayoutBehavior,
    pub control: protocol::ControlProperties,
    pub text_input: protocol::TextInputOptions,
    pub image: Arc<[u8]>,
    pub header: protocol::HeaderOptions,
    pub bottom_navigation: protocol::BottomNavigation,
    pub desktop_navigation: protocol::DesktopNavigation,
    pub contain_scroll: bool,
    pub scroll_axis: u8,
    pub scroll_position: (u32, f32, bool),
    pub layout_observer: u32,
    pub virtual_list: bool,
    pub navigation_transition: u8,
    pub navigation_duration: f32,
    pub navigation_easing: u8,
    pub platform_view: Arc<str>,
    pub children: Vec<u32>,
}
impl Node {
    #[cfg_attr(target_os = "windows", allow(dead_code))]
    pub fn run_style(&self, run: &protocol::TextRun) -> Self {
        let mut node = self.clone();
        node.text_runs = Arc::from([]);
        node.text = Arc::from(run.text.as_str());
        if run.mask & 1 != 0 {
            node.style.font_size = run.font_size;
        }
        if run.mask & 2 != 0 {
            node.style.foreground = run.foreground;
        }
        if run.mask & 4 != 0 {
            node.typography.weight = run.weight;
        }
        if run.mask & 8 != 0 {
            node.typography.italic = run.italic;
        }
        if run.mask & 16 != 0 {
            if run.family != node.typography.family.as_ref() {
                node.font_asset = None;
            }
            node.typography.family = run.family.clone().into();
        }
        if run.mask & 32 != 0 {
            node.typography.decoration = run.decoration;
        }
        node
    }

    pub fn background_asset(&self) -> Self {
        let mut node = self.clone();
        node.kind = Kind::Image;
        node.image = self.background_image.clone();
        node.image_fit = self.background_fit;
        node.image_tint = None;
        node.text = Arc::from("");
        node
    }

    pub fn text_alignment(&self) -> u8 {
        match self.typography.alignment {
            4 => {
                if self.resolved_flow == 2 {
                    2
                } else {
                    0
                }
            }
            5 => {
                if self.resolved_flow == 2 {
                    0
                } else {
                    2
                }
            }
            value => value,
        }
    }
    pub fn padding(&self) -> [f32; 4] {
        let mut p = self
            .extended_layout
            .padding
            .map(|v| v.unwrap_or(self.style.padding));
        let (start, end) = if self.resolved_flow == 2 {
            (2, 0)
        } else {
            (0, 2)
        };
        if let Some(value) = self.advanced_layout.logical[2] {
            p[start] = value;
        }
        if let Some(value) = self.advanced_layout.logical[3] {
            p[end] = value;
        }
        p
    }
}
#[derive(Clone, Default)]
struct Model {
    nodes: im::HashMap<u32, Arc<Node>>,
    root: u32,
    last_id: u32,
    viewport: [f32; 2],
    font_revision: u64,
    font_assets: im::HashMap<u32, crate::font_assets::FontAsset>,
}
impl Model {
    fn apply(&mut self, commands: &[Command<'_>]) -> Result<()> {
        let mut deleted = HashSet::new();
        let mut rich_changed = HashSet::new();
        let mut live = None;
        for command in commands {
            if !matches!(command, Command::Delete(_)) && !deleted.is_empty() {
                self.finish_deletes(&deleted);
                deleted.clear();
                live = None;
            }
            match command {
                Command::Create(id, kind) => {
                    if *id <= self.last_id {
                        return Err(Error::Tree("IDs must increase and never be reused"));
                    }
                    if self.nodes.len() >= MAX_NODES {
                        return Err(Error::Tree("node limit exceeded"));
                    }
                    self.last_id = *id;
                    self.nodes.insert(
                        *id,
                        Arc::new(Node {
                            kind: *kind,
                            text: Arc::from(""),
                            style: UiStyle::default(),
                            layout: protocol::LayoutStyle::default(),
                            advanced_layout: protocol::AdvancedLayout::default(),
                            resolved_flow: 1,
                            extended_layout: protocol::ExtendedLayout::default(),
                            visual: protocol::VisualStyle::default(),
                            projection: [
                                1., 0., 0., 0., 0., 1., 0., 0., 0., 0., 1., 0., 0., 0., 0., 1.,
                            ],
                            gradient: None,
                            background_image: Arc::from([]),
                            background_fit: 1,
                            image_tint: None,
                            image_fit: 0,
                            font_revision: self.font_revision,
                            font_asset: None,
                            font_axes: Arc::from([]),
                            text_runs: Arc::from([]),
                            enabled: true,
                            decoration: protocol::BoxDecoration::default(),
                            z_index: 0,
                            paint_children: vec![],
                            typography: protocol::TypographyStyle::default(),
                            behavior: protocol::LayoutBehavior::default(),
                            control: protocol::ControlProperties::default(),
                            image: Arc::from([]),
                            header: protocol::HeaderOptions::default(),
                            bottom_navigation: protocol::BottomNavigation::default(),
                            desktop_navigation: protocol::DesktopNavigation::default(),
                            text_input: protocol::TextInputOptions::default(),
                            contain_scroll: true,
                            scroll_axis: 0,
                            scroll_position: (0, 0., false),
                            layout_observer: 0,
                            virtual_list: false,
                            navigation_transition: 0,
                            navigation_duration: 0.18,
                            navigation_easing: 1,
                            platform_view: Arc::from(""),
                            children: vec![],
                        }),
                    );
                }
                Command::Text(id, text) => {
                    rich_changed.insert(*id);
                    let n = self.node_mut(*id)?;
                    if n.kind.is_container() && n.kind != Kind::NavigationPage {
                        return Err(Error::Tree("container cannot have text"));
                    }
                    n.text = Arc::from(*text);
                }
                Command::Scroll(id, contain) => {
                    let n = self.node_mut(*id)?;
                    if n.kind != Kind::ScrollView {
                        return Err(Error::Tree("scroll policy requires scroll node"));
                    }
                    n.contain_scroll = *contain;
                }
                Command::HeaderOpacity(id, opacity) => {
                    let n = self.node_mut(*id)?;
                    if !matches!(n.kind, Kind::NavigationPage | Kind::NavigationHeader) {
                        return Err(Error::Tree("header opacity requires navigation"));
                    }
                    n.header.opacity = *opacity;
                }
                Command::DesktopNavigation(id, sidebar) => {
                    let n = self.node_mut(*id)?;
                    if n.kind != Kind::DesktopNavigation {
                        return Err(Error::Tree("sidebar on incompatible node"));
                    }
                    n.desktop_navigation = sidebar.clone();
                }
                Command::BottomNavigation(id, tabs) => {
                    let n = self.node_mut(*id)?;
                    if n.kind != Kind::BottomNavigationBar {
                        return Err(Error::Tree("tabs on incompatible node"));
                    }
                    n.bottom_navigation = tabs.clone();
                }
                Command::Header(id, header) => {
                    let n = self.node_mut(*id)?;
                    if !matches!(n.kind, Kind::NavigationPage | Kind::NavigationHeader) {
                        return Err(Error::Tree("header requires a navigation page or toolbar"));
                    }
                    let opacity = n.header.opacity;
                    n.header = header.clone();
                    n.header.opacity = opacity;
                }
                Command::Style(id, style) => self.node_mut(*id)?.style = *style,
                Command::Children(id, children) => {
                    if children.iter().any(|child| !self.nodes.contains_key(child)) {
                        return Err(Error::Tree("missing child"));
                    }
                    let n = self.node_mut(*id)?;
                    if !n.kind.is_container() {
                        return Err(Error::Tree("leaf cannot have children"));
                    }
                    n.children.clone_from(children);
                }
                Command::Root(id) => {
                    if *id != 0 && !self.nodes.contains_key(id) {
                        return Err(Error::Tree("missing root"));
                    }
                    self.root = *id;
                }
                Command::Delete(id) => {
                    if deleted.contains(id)
                        || *id == self.root
                        || self
                            .node_mut(*id)?
                            .children
                            .iter()
                            .any(|child| !deleted.contains(child))
                    {
                        return Err(Error::Tree("delete requires non-root leaf"));
                    }
                    if live.is_none() {
                        live = Some(self.reachable()?);
                    }
                    if live.as_ref().unwrap().contains(id) {
                        return Err(Error::Tree("delete of attached node"));
                    }
                    // Defer physical unlinking until the end of the deletion run.
                    // This avoids scanning every sibling list for every removed node.
                    deleted.insert(*id);
                }
                Command::VirtualList(id) => {
                    let node = self.node_mut(*id)?;
                    if node.kind != Kind::ScrollView {
                        return Err(Error::Tree("virtual list requires ScrollView"));
                    }
                    node.virtual_list = true;
                }
                Command::ObserveLayout(id, revision) => {
                    self.node_mut(*id)?.layout_observer = *revision;
                }
                Command::ScrollPosition(id, revision, offset, observe) => {
                    let n = self.node_mut(*id)?;
                    if n.kind != Kind::ScrollView {
                        return Err(Error::Tree("scroll position requires ScrollView"));
                    }
                    n.scroll_position = (*revision, *offset, *observe);
                }
                Command::TextInput(id, input) => {
                    let n = self.node_mut(*id)?;
                    if n.kind != Kind::TextField {
                        return Err(Error::Tree("text input options on incompatible node"));
                    }
                    n.text_input = *input;
                }
                Command::Control(id, control) => {
                    let n = self.node_mut(*id)?;
                    if !matches!(
                        n.kind,
                        Kind::TextField
                            | Kind::Switch
                            | Kind::Slider
                            | Kind::RadioButton
                            | Kind::Navigation
                    ) {
                        return Err(Error::Tree("control props on incompatible node"));
                    }
                    if n.kind == Kind::RadioButton && control.value != 0. && control.value != 1. {
                        return Err(Error::Tree("radio selection must be boolean"));
                    }
                    n.control = control.clone();
                }
                Command::Image(id, image) => {
                    let n = self.node_mut(*id)?;
                    if n.kind != Kind::Image {
                        return Err(Error::Tree("image data on incompatible node"));
                    }
                    n.image = Arc::from(*image);
                }
                Command::PlatformView(id, name) => {
                    let n = self.node_mut(*id)?;
                    if n.kind != Kind::PlatformView {
                        return Err(Error::Tree("platform view type requires a platform view"));
                    }
                    n.platform_view = Arc::from(*name);
                }
                Command::Layout(id, layout) => {
                    let n = self.node_mut(*id)?;
                    let shrink = n.layout.shrink;
                    n.layout = *layout;
                    n.layout.shrink = shrink;
                }
                Command::BackgroundImage(id, fit, image) => {
                    let node = self.node_mut(*id)?;
                    if !node.kind.is_container() {
                        return Err(Error::Tree("background image requires a container"));
                    }
                    node.background_image = Arc::from(*image);
                    node.background_fit = *fit;
                }
                Command::ImageTint(id, tint) => {
                    let node = self.node_mut(*id)?;
                    if node.kind != Kind::Image {
                        return Err(Error::Tree("image tint requires an image"));
                    }
                    node.image_tint = *tint;
                }
                Command::ImageFit(id, fit) => {
                    let node = self.node_mut(*id)?;
                    if node.kind != Kind::Image {
                        return Err(Error::Tree("image fit requires an image"));
                    }
                    node.image_fit = *fit;
                }
                Command::RegisterFont(key, asset) => {
                    if self.font_assets.contains_key(key) {
                        return Err(Error::Tree("font registration key already exists"));
                    }
                    if self.font_assets.len() >= 64
                        || self
                            .font_assets
                            .values()
                            .map(|font| font.data.len())
                            .sum::<usize>()
                            + asset.data.len()
                            > 32 * 1024 * 1024
                    {
                        return Err(Error::Tree("font registration budget exceeded"));
                    }
                    self.font_assets.insert(*key, asset.clone());
                }
                Command::FontReference(id, key) => {
                    let asset = self
                        .font_assets
                        .get(key)
                        .cloned()
                        .ok_or(Error::Tree("unknown font registration key"))?;
                    let node = self.node_mut(*id)?;
                    if !matches!(
                        node.kind,
                        Kind::Text | Kind::Button | Kind::TextField | Kind::RadioButton
                    ) {
                        return Err(Error::Tree("font reference requires textual control"));
                    }
                    node.font_asset = Some(asset);
                }
                Command::FontSource(id, asset) => {
                    let node = self.node_mut(*id)?;
                    if !matches!(
                        node.kind,
                        Kind::Text | Kind::Button | Kind::TextField | Kind::RadioButton
                    ) {
                        return Err(Error::Tree("font source requires textual control"));
                    }
                    node.font_asset = asset.clone();
                }
                Command::FontVariations(id, axes) => {
                    let node = self.node_mut(*id)?;
                    if !matches!(
                        node.kind,
                        Kind::Text | Kind::Button | Kind::TextField | Kind::RadioButton
                    ) {
                        return Err(Error::Tree("font axes require a textual control"));
                    }
                    node.font_axes = Arc::from(axes.clone());
                }
                Command::TextRuns(id, runs) => {
                    rich_changed.insert(*id);
                    let node = self.node_mut(*id)?;
                    if node.kind != Kind::Text {
                        return Err(Error::Tree("text runs require Text"));
                    }
                    node.text_runs = Arc::from(runs.clone());
                }
                Command::Enabled(id, value) => {
                    let node = self.node_mut(*id)?;
                    if !matches!(
                        node.kind,
                        Kind::Button
                            | Kind::TextField
                            | Kind::Switch
                            | Kind::Slider
                            | Kind::RadioButton
                            | Kind::BottomNavigationBar
                            | Kind::DesktopNavigation
                    ) {
                        return Err(Error::Tree("enabled requires interactive control"));
                    }
                    node.enabled = *value;
                }
                Command::BoxDecoration(id, value) => self.node_mut(*id)?.decoration = *value,
                Command::ZIndex(id, value) => self.node_mut(*id)?.z_index = *value,
                Command::Gradient(id, gradient) => {
                    let node = self.node_mut(*id)?;
                    if !node.kind.is_container() {
                        return Err(Error::Tree("gradient requires a container"));
                    }
                    node.gradient = gradient.clone();
                }
                Command::AdvancedLayout(id, style) => {
                    self.node_mut(*id)?.advanced_layout = style.clone()
                }
                Command::LayoutBehavior(id, style) => self.node_mut(*id)?.behavior = *style,
                Command::Typography(id, style) => self.node_mut(*id)?.typography = style.clone(),
                Command::Projection(id, matrix) => self.node_mut(*id)?.projection = *matrix,
                Command::Visual(id, style) => self.node_mut(*id)?.visual = *style,
                Command::ExtendedLayout(id, layout) => {
                    self.node_mut(*id)?.extended_layout = *layout
                }
                Command::FlexShrink(id, shrink) => self.node_mut(*id)?.layout.shrink = *shrink,
                Command::ScrollAxis(id, axis) => {
                    let n = self.node_mut(*id)?;
                    if n.kind != Kind::ScrollView {
                        return Err(Error::Tree("scroll axis requires scroll node"));
                    }
                    n.scroll_axis = *axis;
                }
                Command::NavigationTransition(id, kind, duration, easing) => {
                    let n = self.node_mut(*id)?;
                    if n.kind != Kind::Navigation {
                        return Err(Error::Tree("transition requires navigation"));
                    }
                    n.navigation_transition = *kind;
                    n.navigation_duration = *duration;
                    n.navigation_easing = *easing;
                }
                Command::Viewport(viewport) => self.viewport = *viewport,
            }
        }
        self.finish_deletes(&deleted);
        for (id, node) in &self.nodes {
            if node.kind == Kind::DesktopNavigation
                && (node.children.len() != 1
                    || !(180. ..=1000.).contains(&node.desktop_navigation.width)
                    || node.desktop_navigation.selected < -1
                    || node.desktop_navigation.selected
                        >= node.desktop_navigation.items.len() as i32)
            {
                return Err(Error::Tree("invalid desktop navigation"));
            }
            if node.kind == Kind::BottomNavigationBar
                && (!(2..=5).contains(&node.bottom_navigation.items.len())
                    || node.bottom_navigation.selected >= node.bottom_navigation.items.len())
            {
                return Err(Error::Tree("invalid bottom navigation"));
            }
            if node.kind == Kind::Navigation
                && ((*id != self.root
                    && !self.nodes.get(&self.root).is_some_and(|root| {
                        root.kind == Kind::DesktopNavigation && root.children.as_slice() == [*id]
                    }))
                    || node.children.is_empty()
                    || node.control.value < 1.
                    || node.control.focus_revision > 2
                    || node.control.value.fract() != 0.
                    || node.control.value as usize > node.children.len()
                    || node
                        .children
                        .iter()
                        .any(|child| self.nodes[child].kind != Kind::NavigationPage))
            {
                return Err(Error::Tree("invalid navigation root"));
            }
        }
        if commands.iter().any(|c| {
            matches!(
                c,
                Command::FontSource(_, Some(_)) | Command::RegisterFont(_, _)
            )
        }) {
            self.font_revision = self
                .font_revision
                .checked_add(1)
                .ok_or(Error::Tree("font revision overflow"))?;
            let ids: Vec<_> = self.nodes.keys().copied().collect();
            for id in ids {
                let revision = self.font_revision;
                self.node_mut(id)?.font_revision = revision;
            }
        }
        for node in self.nodes.values() {
            if let Some(asset) = &node.font_asset {
                if !asset.family.eq_ignore_ascii_case(&node.typography.family) {
                    return Err(Error::Tree("font source and typography families differ"));
                }
            }
        }
        for id in rich_changed {
            let Some(node) = self.nodes.get(&id) else {
                continue;
            };
            if !node.text_runs.is_empty()
                && node
                    .text_runs
                    .iter()
                    .map(|r| r.text.as_str())
                    .collect::<String>()
                    != node.text.as_ref()
            {
                return Err(Error::Tree("text runs must match node text"));
            }
        }
        if self.reachable()?.len() != self.nodes.len() {
            return Err(Error::Tree("unreachable nodes"));
        }
        let mut pending = if self.root == 0 {
            vec![]
        } else {
            vec![(self.root, 1)]
        };
        while let Some((id, inherited)) = pending.pop() {
            let node = &self.nodes[&id];
            let flow = if node.advanced_layout.flow == 0 {
                inherited
            } else {
                node.advanced_layout.flow
            };
            pending.extend(node.children.iter().map(|child| (*child, flow)));
            let mut painting = node.children.clone();
            if node.kind == Kind::Navigation
                && painting.iter().any(|id| self.nodes[id].z_index != 0)
            {
                return Err(Error::Tree("navigation pages cannot override stack order"));
            }
            painting.sort_by_key(|id| self.nodes[id].z_index);
            let flow_changed = node.resolved_flow != flow;
            if painting != node.paint_children {
                self.node_mut(id)?.paint_children = painting;
            }
            if flow_changed {
                self.node_mut(id)?.resolved_flow = flow;
            }
        }
        Ok(())
    }
    fn finish_deletes(&mut self, deleted: &HashSet<u32>) {
        if deleted.is_empty() {
            return;
        }
        for id in deleted {
            self.nodes.remove(id);
        }
        let parents: Vec<_> = self
            .nodes
            .iter()
            .filter(|(_, node)| node.children.iter().any(|id| deleted.contains(id)))
            .map(|(id, _)| *id)
            .collect();
        for id in parents {
            Arc::make_mut(self.nodes.get_mut(&id).unwrap())
                .children
                .retain(|id| !deleted.contains(id));
        }
    }
    fn node_mut(&mut self, id: u32) -> Result<&mut Node> {
        self.nodes
            .get_mut(&id)
            .map(Arc::make_mut)
            .ok_or(Error::Tree("unknown node ID"))
    }
    fn reachable(&self) -> Result<HashSet<u32>> {
        let mut seen = HashSet::new();
        let mut pending = if self.root == 0 {
            vec![]
        } else {
            vec![(self.root, 0)]
        };
        while let Some((id, depth)) = pending.pop() {
            if depth > MAX_DEPTH {
                return Err(Error::Tree("depth limit exceeded"));
            }
            if !seen.insert(id) {
                return Err(Error::Tree("cycle or duplicate parent"));
            }
            let n = self.nodes.get(&id).ok_or(Error::Tree("missing node"))?;
            pending.extend(n.children.iter().map(|child| (*child, depth + 1)));
        }
        Ok(seen)
    }
}

/// The Step 4 platform measures native leaf CONTENT only, excluding protocol
/// padding (Taffy adds that exactly once). Respect known dimensions and available
/// content width when measuring multiline text. No video/pixel buffers enter here.
pub trait Measure {
    /// Validate platform-specific style support before committing any state.
    fn validate_style(&self, _node: &Node) -> Result<()> {
        Ok(())
    }
    /// Space occupied by non-overlay native scrollbars, in logical units.
    fn scrollbar_width(&self) -> f32 {
        0.
    }
    /// Bottom system gesture inset; native bottom navigation owns it when present.
    fn bottom_safe_area(&self) -> f32 {
        0.
    }
    fn navigation_bar_height(&mut self, _width: f32) -> f32 {
        0.
    }
    fn measure(
        &mut self,
        node: &Node,
        known: Size<Option<f32>>,
        available: Size<AvailableSpace>,
    ) -> Result<Size<f32>>;
}
/// Production-safe default: refuse leaf layout until a native measurer exists.
/// There is deliberately no character-count approximation masquerading as typography.
pub struct RequireNativeMeasure;
impl Measure for RequireNativeMeasure {
    fn measure(
        &mut self,
        _: &Node,
        _: Size<Option<f32>>,
        _: Size<AvailableSpace>,
    ) -> Result<Size<f32>> {
        Err(Error::Measurement(
            "native leaf measurer is not installed (Step 4)",
        ))
    }
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Frame {
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
}

pub struct Engine {
    model: Model,
    tree: TaffyTree<u32>,
    ids: HashMap<u32, NodeId>,
    measure: Box<dyn Measure>,
    spare: Option<(Model, TaffyTree<u32>, HashMap<u32, NodeId>)>,
    reported_layouts: HashMap<u32, (u32, f32, f32)>,
    #[cfg(test)]
    pub(crate) layout_passes: usize,
}
impl Engine {
    pub fn new(measure: Box<dyn Measure>) -> Self {
        let mut tree = TaffyTree::new();
        // Logical coordinates: pixel snapping belongs to the native display scale.
        tree.disable_rounding();
        Self {
            model: Model::default(),
            tree,
            ids: HashMap::new(),
            measure,
            spare: None,
            reported_layouts: HashMap::new(),
            #[cfg(test)]
            layout_passes: 0,
        }
    }
    pub fn node(&self, id: u32) -> Option<&Node> {
        self.model.nodes.get(&id).map(Arc::as_ref)
    }
    /// Active committed nodes; iteration order is unspecified.
    pub fn nodes(&self) -> impl Iterator<Item = (u32, &Node)> {
        self.model
            .nodes
            .iter()
            .map(|(id, node)| (*id, node.as_ref()))
    }
    pub fn root(&self) -> u32 {
        self.model.root
    }
    /// O(1) shared presentation snapshot. Unchanged payloads are never deep-cloned.
    pub(crate) fn presentation_nodes(&self) -> im::HashMap<u32, Arc<Node>> {
        self.model.nodes.clone()
    }
    pub fn node_count(&self) -> usize {
        self.model.nodes.len()
    }
    /// Enqueue committed geometry before presentation scroll events. No managed callback
    /// runs until the native flush returns successfully. Deduplicated across commits.
    pub(crate) fn report_layouts(&mut self) -> Result<()> {
        let mut current = HashMap::new();
        for (id, node) in self.nodes() {
            if node.layout_observer == 0 {
                continue;
            }
            let frame = self.frame(id)?;
            let value = (node.layout_observer, frame.width, frame.height);
            if self.reported_layouts.get(&id) != Some(&value) {
                crate::events::emit(
                    id,
                    11,
                    frame.height,
                    format!("{}:{}", node.layout_observer, frame.width),
                );
            }
            current.insert(id, value);
        }
        self.reported_layouts = current;
        Ok(())
    }
    pub fn frame(&self, id: u32) -> Result<Frame> {
        let id = self.ids.get(&id).ok_or(Error::Tree("unknown frame ID"))?;
        let layout = self.tree.layout(*id)?;
        Ok(Frame {
            x: layout.location.x,
            y: layout.location.y,
            width: layout.size.width,
            height: layout.size.height,
        })
    }
    /// Parses borrowed bytes, validates a candidate, and commits only a fully
    /// laid-out tree. Old state survives errors. The native measurer must not
    /// perform presentation mutations; those belong after successful commit.
    pub fn flush(&mut self, bytes: &[u8]) -> Result<()> {
        let commands = protocol::parse(bytes)?;
        let mut model = self.model.clone();
        model.apply(&commands)?;
        for (id, node) in &model.nodes {
            if node.virtual_list {
                if node.scroll_axis != 0
                    || node.children.len() != 1
                    || model
                        .nodes
                        .get(&node.children[0])
                        .is_none_or(|n| n.kind != Kind::VStack)
                {
                    return Err(Error::Tree(
                        "virtual list requires vertical scrolling and one VStack document",
                    ));
                }
                if self
                    .model
                    .nodes
                    .get(id)
                    .is_some_and(|old| !old.virtual_list)
                {
                    return Err(Error::Tree("changing scroll backend requires a new node"));
                }
            }
            if node.kind == Kind::TextField
                && self
                    .model
                    .nodes
                    .get(id)
                    .is_some_and(|old| old.text_input.multiline() != node.text_input.multiline())
            {
                return Err(Error::Tree(
                    "changing multiline mode requires a new text field node",
                ));
            }
            if self
                .model
                .nodes
                .get(id)
                .is_none_or(|old| !Arc::ptr_eq(old, node))
            {
                self.measure.validate_style(node)?;
            }
        }
        // Background assets do not participate in layout, but decoding must still be transactional.
        for (id, node) in &model.nodes {
            if !node.background_image.is_empty()
                && self
                    .model
                    .nodes
                    .get(id)
                    .is_none_or(|old| old.background_image != node.background_image)
            {
                self.measure.measure(
                    &node.background_asset(),
                    Size {
                        width: None,
                        height: None,
                    },
                    Size {
                        width: AvailableSpace::MaxContent,
                        height: AvailableSpace::MaxContent,
                    },
                )?;
            }
        }
        // Presentation-only commits keep the active layout and the spare's
        // original baseline paired. A later geometry batch still reconciles
        // against the generation that actually produced that spare tree.
        let needs_layout = commands.iter().any(|command| match command {
            Command::Scroll(..)
            | Command::Projection(..)
            | Command::Visual(..)
            | Command::Gradient(..)
            | Command::ZIndex(..)
            | Command::ImageFit(..)
            | Command::ImageTint(..)
            | Command::BackgroundImage(..)
            | Command::BoxDecoration(..)
            | Command::Enabled(..)
            | Command::NavigationTransition(..)
            | Command::BottomNavigation(..)
            | Command::DesktopNavigation(..)
            | Command::ScrollPosition(..)
            | Command::ObserveLayout(..)
            | Command::VirtualList(..)
            | Command::PlatformView(..) => false,
            Command::Control(id, _) => {
                let old = self.model.nodes.get(id);
                let new = model.nodes.get(id);
                match (old, new) {
                    (Some(old), Some(new)) => {
                        new.kind == Kind::TextField
                            && old.control.placeholder != new.control.placeholder
                    }
                    _ => true,
                }
            }
            Command::HeaderOpacity(id, _) => {
                match (self.model.nodes.get(id), model.nodes.get(id)) {
                    (Some(old), Some(new)) => {
                        new.kind == Kind::NavigationPage
                            && (old.header.opacity == 1.) != (new.header.opacity == 1.)
                    }
                    _ => true,
                }
            }
            // Header visibility/title mode and native metrics can change page
            // geometry. Keep these conservative until metrics are explicit.
            _ => true,
        });
        if !needs_layout {
            self.model = model;
            return Ok(());
        }
        // Fixed-size image nodes may bypass Taffy's measurement callback. Decode
        // changed assets before commit so invalid data cannot reach presentation.
        for (id, node) in &model.nodes {
            if matches!(node.kind, Kind::Image | Kind::Icon)
                && self
                    .model
                    .nodes
                    .get(id)
                    .is_none_or(|old| old.image != node.image || old.text != node.text)
            {
                self.measure.measure(
                    node,
                    Size {
                        width: None,
                        height: None,
                    },
                    Size {
                        width: AvailableSpace::MaxContent,
                        height: AvailableSpace::MaxContent,
                    },
                )?;
            }
        }
        // Two committed layout generations alternate. A failed candidate is
        // discarded; the active tree remains intact and seeds the next attempt.
        let (baseline, mut tree, mut ids) = self
            .spare
            .take()
            .unwrap_or_else(|| (self.model.clone(), self.tree.clone(), self.ids.clone()));
        // Unlink removed hierarchies first, before reparenting/removing Taffy IDs.
        for (id, old) in &baseline.nodes {
            if model
                .nodes
                .get(id)
                .is_none_or(|new| new.children != old.children)
            {
                tree.set_children(ids[id], &[])?;
            }
        }
        for id in baseline
            .nodes
            .keys()
            .filter(|id| !model.nodes.contains_key(id))
        {
            tree.remove(ids.remove(id).unwrap())?;
        }
        // Scroll pages fill the navigation viewport. UIKit animates their
        // adjusted content inset together with large/inline header transitions.
        // Subtracting a static bar height here gives the two pages incompatible
        // geometry while the native header is still animating.
        let bottom_safe_area = self.measure.bottom_safe_area();
        let owns_bottom = |root: u32| {
            let mut pending = vec![root];
            while let Some(id) = pending.pop() {
                if let Some(node) = model.nodes.get(&id) {
                    if node.kind == Kind::BottomNavigationBar {
                        return true;
                    }
                    pending.extend(node.children.iter().copied());
                }
            }
            false
        };
        // A desktop shell can own a single navigation host. Its reserved pane
        // is outside the page viewport; pages must not be sized to the OS window.
        let navigation_viewport = model
            .nodes
            .get(&model.root)
            .filter(|root| root.kind == Kind::DesktopNavigation)
            .map(|root| {
                let [left, top, right, bottom] = root.padding();
                [
                    (model.viewport[0] - left - right).max(0.),
                    (model.viewport[1] - top - bottom).max(0.),
                ]
            })
            .unwrap_or(model.viewport);
        let mut page_bottom_insets = HashMap::new();
        let mut page_content = HashMap::new();
        for (id, node) in &model.nodes {
            if node.kind != Kind::NavigationPage {
                continue;
            }
            let inset = if bottom_safe_area == 0. || owns_bottom(*id) {
                0.
            } else {
                bottom_safe_area
            };
            page_bottom_insets.insert(*id, inset);
            for child in &node.children {
                page_bottom_insets.insert(*child, inset);
            }
            let scroll_root = node
                .children
                .first()
                .and_then(|id| model.nodes.get(id))
                .is_some_and(|child| child.kind == Kind::ScrollView);
            let subtract_bar = node.header.visible
                && !scroll_root
                && !node.header.large_title
                && node.header.opacity == 1.;
            page_content.insert(*id, subtract_bar);
            for child in &node.children {
                page_content.insert(*child, subtract_bar);
            }
        }
        let bar_height = if model
            .nodes
            .values()
            .any(|node| node.kind == Kind::Navigation)
        {
            self.measure.navigation_bar_height(model.viewport[0])
        } else {
            0.
        };
        let stable_context = bottom_safe_area == 0.
            && model.viewport == baseline.viewport
            && model.root == baseline.root
            && page_content.is_empty();
        for (id, node) in &model.nodes {
            // Shared nodes already have the right style and measurement cache.
            // Navigation geometry also depends on native bar metrics, so do not
            // take this shortcut for navigation pages or their content.
            if stable_context
                && baseline
                    .nodes
                    .get(id)
                    .is_some_and(|old| Arc::ptr_eq(old, node))
            {
                continue;
            }
            let mut root_viewport = model.viewport;
            if bottom_safe_area > 0.
                && *id == model.root
                && node.kind != Kind::Navigation
                && !owns_bottom(*id)
            {
                root_viewport[1] = (root_viewport[1] - bottom_safe_area).max(0.);
            }
            let mut style = layout_style(node, *id == model.root, root_viewport);
            style.scrollbar_width = self.measure.scrollbar_width();
            if node.kind == Kind::Navigation && *id != model.root {
                style.size = Size {
                    width: Dimension::length(navigation_viewport[0]),
                    height: Dimension::length(navigation_viewport[1]),
                };
            }
            if node.kind == Kind::NavigationPage || page_content.contains_key(id) {
                if node.kind == Kind::NavigationPage {
                    style.position = Position::Absolute;
                }
                style.size = Size {
                    width: Dimension::length(navigation_viewport[0]),
                    height: Dimension::length(
                        (navigation_viewport[1]
                            - page_bottom_insets.get(id).copied().unwrap_or(0.)
                            - if page_content.get(id).copied().unwrap_or(
                                node.header.visible
                                    && !node.header.large_title
                                    && node.header.opacity == 1.,
                            ) {
                                bar_height
                            } else {
                                0.
                            })
                        .max(0.),
                    ),
                };
            }
            if let Some(taffy_id) = ids.get(id).copied() {
                if tree.style(taffy_id)? != &style {
                    tree.set_style(taffy_id, style)?;
                }
                if baseline.nodes.get(id).is_some_and(|old| {
                    old.text != node.text
                        || old.font_revision != node.font_revision
                        || old.font_asset != node.font_asset
                        || old.font_axes != node.font_axes
                        || old.text_runs != node.text_runs
                        || old.typography != node.typography
                        || old.style != node.style
                        || old.control != node.control
                        || old.text_input != node.text_input
                        || old.image != node.image
                }) {
                    tree.mark_dirty(taffy_id)?;
                }
            } else {
                let taffy_id = if node.kind.is_container() {
                    tree.new_leaf(style)?
                } else {
                    tree.new_leaf_with_context(style, *id)?
                };
                ids.insert(*id, taffy_id);
            }
        }
        for (id, node) in &model.nodes {
            if baseline
                .nodes
                .get(id)
                .is_none_or(|old| old.children != node.children)
            {
                let children: Vec<_> = node.children.iter().map(|id| ids[id]).collect();
                tree.set_children(ids[id], &children)?;
            }
        }
        if model.root != 0 {
            #[cfg(test)]
            {
                self.layout_passes += 1;
            }
            let mut failure = None;
            tree.compute_layout_with_measure(
                ids[&model.root],
                Size {
                    width: AvailableSpace::Definite(model.viewport[0]),
                    height: AvailableSpace::Definite(model.viewport[1]),
                },
                |known, available, _, context, _| {
                    // A native measurer may leave a pending JNI exception. Once
                    // it fails, do not make additional native calls in this batch.
                    if failure.is_some() {
                        return Size::ZERO;
                    }
                    let Some(id) = context else {
                        return Size::ZERO;
                    };
                    match self.measure.measure(&model.nodes[id], known, available) {
                        Ok(size)
                            if size.width.is_finite()
                                && size.height.is_finite()
                                && size.width >= 0.
                                && size.height >= 0. =>
                        {
                            size
                        }
                        Ok(size) => {
                            eprintln!("Invalid native measurement: node {id}, kind {:?}, known {known:?}, available {available:?}, result {size:?}", model.nodes[id].kind);
                            failure = Some(Error::Measurement("invalid measured size"));
                            Size::ZERO
                        }
                        Err(error) => {
                            failure = Some(error);
                            Size::ZERO
                        }
                    }
                },
            )?;
            if let Some(error) = failure {
                return Err(error);
            }
            for id in ids.values() {
                let l = tree.layout(*id)?;
                if ![l.location.x, l.location.y, l.size.width, l.size.height]
                    .iter()
                    .all(|v| v.is_finite())
                {
                    return Err(Error::Measurement("layout overflow"));
                }
            }
        }
        self.spare = Some((
            std::mem::replace(&mut self.model, model),
            std::mem::replace(&mut self.tree, tree),
            std::mem::replace(&mut self.ids, ids),
        ));
        Ok(())
    }
}
fn layout_style(node: &Node, root: bool, viewport: [f32; 2]) -> taffy::Style {
    let a = &node.advanced_layout;
    let mut margin = node.extended_layout.margin;
    let mut insets = node.behavior.insets;
    let (start, end) = if node.resolved_flow == 2 {
        (2, 0)
    } else {
        (0, 2)
    };
    if let Some(v) = a.logical[0] {
        margin[start] = v;
    }
    if let Some(v) = a.logical[1] {
        margin[end] = v;
    }
    if let Some(v) = a.logical[4] {
        insets[start] = Some(v);
    }
    if let Some(v) = a.logical[5] {
        insets[end] = Some(v);
    }
    let tracks = |tracks: &[protocol::GridTrack]| {
        tracks
            .iter()
            .map(|t| match t.unit {
                1 => length(t.value),
                2 => percent(t.value / 100.),
                3 => fr(t.value),
                _ => auto(),
            })
            .collect()
    };
    let placement = |start: u16, count: u16| taffy::geometry::Line {
        start: if start == 0 {
            GridPlacement::Auto
        } else {
            line(start as i16)
        },
        end: span(count),
    };
    taffy::Style {
        direction: if node.resolved_flow == 2 {
            taffy::style::Direction::Rtl
        } else {
            taffy::style::Direction::Ltr
        },
        grid_template_columns: tracks(&a.columns),
        grid_template_rows: tracks(&a.rows),
        grid_column: placement(a.column, a.column_span),
        grid_row: placement(a.row, a.row_span),
        align_content: match a.align_content {
            1 => Some(AlignContent::START),
            2 => Some(AlignContent::CENTER),
            3 => Some(AlignContent::END),
            4 => Some(AlignContent::STRETCH),
            5 => Some(AlignContent::SPACE_BETWEEN),
            6 => Some(AlignContent::SPACE_AROUND),
            7 => Some(AlignContent::SPACE_EVENLY),
            _ => None,
        },
        display: if node.behavior.visibility == 2 {
            Display::None
        } else if a.grid {
            Display::Grid
        } else {
            Display::Flex
        },
        position: if node.behavior.position == 1 {
            Position::Absolute
        } else {
            Position::Relative
        },
        aspect_ratio: node.behavior.aspect_ratio,
        inset: Rect {
            left: insets[0]
                .map(LengthPercentageAuto::length)
                .unwrap_or(LengthPercentageAuto::auto()),
            top: insets[1]
                .map(LengthPercentageAuto::length)
                .unwrap_or(LengthPercentageAuto::auto()),
            right: insets[2]
                .map(LengthPercentageAuto::length)
                .unwrap_or(LengthPercentageAuto::auto()),
            bottom: insets[3]
                .map(LengthPercentageAuto::length)
                .unwrap_or(LengthPercentageAuto::auto()),
        },
        overflow: if node.kind == Kind::ScrollView {
            Point {
                x: if node.scroll_axis == 1 {
                    Overflow::Scroll
                } else {
                    Overflow::Hidden
                },
                y: if node.scroll_axis == 0 {
                    Overflow::Scroll
                } else {
                    Overflow::Hidden
                },
            }
        } else {
            Point {
                x: Overflow::Visible,
                y: Overflow::Visible,
            }
        },
        flex_direction: if (if node.kind == Kind::ScrollView {
            node.scroll_axis
        } else {
            node.layout.direction
        }) == 0
        {
            if a.reverse {
                FlexDirection::ColumnReverse
            } else {
                FlexDirection::Column
            }
        } else {
            if a.reverse {
                FlexDirection::RowReverse
            } else {
                FlexDirection::Row
            }
        },
        flex_grow: node.layout.grow,
        gap: Size {
            width: LengthPercentage::length(a.column_gap.unwrap_or(node.layout.gap)),
            height: LengthPercentage::length(a.row_gap.unwrap_or(node.layout.gap)),
        },
        justify_content: Some(match node.layout.main_alignment {
            1 => JustifyContent::CENTER,
            2 => JustifyContent::END,
            3 => JustifyContent::SPACE_BETWEEN,
            4 => JustifyContent::SPACE_AROUND,
            5 => JustifyContent::SPACE_EVENLY,
            _ => JustifyContent::START,
        }),
        align_items: Some(match node.layout.cross_alignment {
            1 => AlignItems::START,
            2 => AlignItems::CENTER,
            3 => AlignItems::END,
            _ => AlignItems::STRETCH,
        }),
        flex_shrink: node.layout.shrink,
        min_size: Size {
            width: node
                .extended_layout
                .min_width
                .map(Dimension::length)
                .unwrap_or(if node.layout.shrink > 0. {
                    Dimension::length(0.)
                } else {
                    Dimension::auto()
                }),
            height: node
                .extended_layout
                .min_height
                .map(Dimension::length)
                .unwrap_or(if node.layout.shrink > 0. {
                    Dimension::length(0.)
                } else {
                    Dimension::auto()
                }),
        },
        max_size: Size {
            width: node
                .extended_layout
                .max_width
                .map(Dimension::length)
                .unwrap_or(Dimension::auto()),
            height: node
                .extended_layout
                .max_height
                .map(Dimension::length)
                .unwrap_or(Dimension::auto()),
        },
        margin: Rect {
            left: LengthPercentageAuto::length(margin[0]),
            top: LengthPercentageAuto::length(margin[1]),
            right: LengthPercentageAuto::length(margin[2]),
            bottom: LengthPercentageAuto::length(margin[3]),
        },
        flex_basis: node
            .extended_layout
            .basis
            .map(Dimension::length)
            .unwrap_or(Dimension::auto()),
        flex_wrap: match node.extended_layout.wrap {
            1 => FlexWrap::Wrap,
            2 => FlexWrap::WrapReverse,
            _ => FlexWrap::NoWrap,
        },
        align_self: match node.extended_layout.align_self {
            1 => Some(AlignSelf::STRETCH),
            2 => Some(AlignSelf::START),
            3 => Some(AlignSelf::CENTER),
            4 => Some(AlignSelf::END),
            _ => None,
        },
        padding: Rect {
            left: LengthPercentage::length(node.padding()[0]),
            top: LengthPercentage::length(node.padding()[1]),
            right: LengthPercentage::length(node.padding()[2]),
            bottom: LengthPercentage::length(node.padding()[3]),
        },
        size: if root {
            Size {
                width: Dimension::length(viewport[0]),
                height: Dimension::length(viewport[1]),
            }
        } else {
            Size {
                width: node
                    .behavior
                    .width_percent
                    .map(|v| Dimension::percent(v / 100.))
                    .or_else(|| node.layout.width.map(Dimension::length))
                    .unwrap_or(Dimension::auto()),
                height: node
                    .behavior
                    .height_percent
                    .map(|v| Dimension::percent(v / 100.))
                    .or_else(|| node.layout.height.map(Dimension::length))
                    .unwrap_or(Dimension::auto()),
            }
        },
        ..Default::default()
    }
}
