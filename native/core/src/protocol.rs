use crate::{Error, Result};

pub const MAX_BYTES: usize = 16 * 1024 * 1024;
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Kind {
    VStack,
    Text,
    Button,
    ScrollView,
    TextField,
    Switch,
    Slider,
    Image,
    Icon,
    Navigation,
    NavigationPage,
    RadioButton,
    NavigationHeader,
    BottomNavigationBar,
    DesktopNavigation,
    PlatformView,
}
impl Kind {
    pub fn is_container(self) -> bool {
        matches!(
            self,
            Self::VStack
                | Self::ScrollView
                | Self::Navigation
                | Self::NavigationPage
                | Self::DesktopNavigation
        )
    }
    pub fn wire_id(self) -> i32 {
        match self {
            Self::VStack => 1,
            Self::Text => 2,
            Self::Button => 3,
            Self::ScrollView => 4,
            Self::TextField => 5,
            Self::Switch => 6,
            Self::Slider => 7,
            Self::Image => 8,
            Self::Icon => 9,
            Self::Navigation => 10,
            Self::NavigationPage => 11,
            Self::RadioButton => 12,
            Self::NavigationHeader => 13,
            Self::BottomNavigationBar => 14,
            Self::DesktopNavigation => 15,
            Self::PlatformView => 16,
        }
    }
}
#[derive(Clone, Debug, PartialEq, Default)]
pub struct DesktopNavigation {
    pub theme: u32,
    pub selected: i32,
    pub open: bool,
    pub width: f32,
    pub title: std::sync::Arc<str>,
    pub items: Vec<(std::sync::Arc<str>, std::sync::Arc<str>)>,
}
#[derive(Clone, Debug, PartialEq, Default)]
pub struct BottomNavigation {
    pub selected: usize,
    pub items: Vec<(u32, std::sync::Arc<str>)>,
}
#[derive(Clone, Debug, PartialEq)]
pub struct HeaderOptions {
    pub visible: bool,
    pub back_visible: bool,
    pub can_pop: bool,
    pub large_title: bool,
    pub opacity: f32,
    pub leading: usize,
    pub actions: Vec<std::sync::Arc<str>>,
}
impl Default for HeaderOptions {
    fn default() -> Self {
        Self {
            visible: true,
            back_visible: true,
            can_pop: false,
            large_title: false,
            opacity: 1.,
            leading: 0,
            actions: vec![],
        }
    }
}
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct TextInputOptions {
    pub keyboard: u8,
    pub action: u8,
    pub capitalization: u8,
    pub autofill: u8,
    pub flags: u8,
    pub placeholder_color: Option<[u8; 4]>,
}
impl Default for TextInputOptions {
    fn default() -> Self {
        Self {
            keyboard: 0,
            action: 0,
            capitalization: 0,
            autofill: 0,
            flags: 4,
            placeholder_color: None,
        }
    }
}
impl TextInputOptions {
    pub fn password(self) -> bool {
        self.flags & 1 != 0
    }
    pub fn multiline(self) -> bool {
        self.flags & 2 != 0
    }
    pub fn autocorrect(self) -> bool {
        self.flags & 4 != 0 && !self.password()
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct ControlProperties {
    pub value: f32,
    pub minimum: f32,
    pub maximum: f32,
    pub focus_revision: u32,
    pub focus_requested: bool,
    pub placeholder: std::sync::Arc<str>,
}
impl Default for ControlProperties {
    fn default() -> Self {
        Self {
            value: 0.,
            minimum: 0.,
            maximum: 1.,
            focus_revision: 0,
            focus_requested: false,
            placeholder: std::sync::Arc::from(""),
        }
    }
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Style {
    pub padding: f32,
    pub background: [u8; 4],
    pub radius: f32,
    pub foreground: [u8; 4],
    pub font_size: f32,
}
impl Default for Style {
    fn default() -> Self {
        Self {
            padding: 0.,
            background: [0; 4],
            radius: 0.,
            foreground: [0, 0, 0, 255],
            font_size: 17.,
        }
    }
}
#[derive(Clone, Copy, Debug, PartialEq, Default)]
pub struct LayoutStyle {
    pub width: Option<f32>,
    pub height: Option<f32>,
    pub gap: f32,
    pub grow: f32,
    pub shrink: f32,
    pub direction: u8,
    pub main_alignment: u8,
    pub cross_alignment: u8,
}
#[derive(Clone, Copy, Debug, PartialEq, Default)]
pub struct ExtendedLayout {
    pub margin: [f32; 4],
    pub min_width: Option<f32>,
    pub min_height: Option<f32>,
    pub max_width: Option<f32>,
    pub max_height: Option<f32>,
    pub basis: Option<f32>,
    pub wrap: u8,
    pub align_self: u8,
    pub padding: [Option<f32>; 4],
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct VisualStyle {
    pub opacity: f32,
    pub border_width: f32,
    pub border_color: [u8; 4],
    pub shadow_color: [u8; 4],
    pub shadow_blur: f32,
    pub shadow_x: f32,
    pub shadow_y: f32,
    pub translate_x: f32,
    pub translate_y: f32,
    pub scale_x: f32,
    pub scale_y: f32,
    pub rotation: f32,
    pub clip: bool,
}
impl Default for VisualStyle {
    fn default() -> Self {
        Self {
            opacity: 1.,
            border_width: 0.,
            border_color: [0; 4],
            shadow_color: [0; 4],
            shadow_blur: 0.,
            shadow_x: 0.,
            shadow_y: 0.,
            translate_x: 0.,
            translate_y: 0.,
            scale_x: 1.,
            scale_y: 1.,
            rotation: 0.,
            clip: true,
        }
    }
}
#[derive(Clone, Debug, PartialEq)]
pub struct TypographyStyle {
    pub family: std::sync::Arc<str>,
    pub weight: u16,
    pub italic: bool,
    pub alignment: u8,
    pub max_lines: i32,
    pub overflow: u8,
    pub letter_spacing: f32,
    pub line_height: f32,
    pub decoration: u8,
}
impl Default for TypographyStyle {
    fn default() -> Self {
        Self {
            family: std::sync::Arc::from(""),
            weight: 400,
            italic: false,
            alignment: 0,
            max_lines: -1,
            overflow: 0,
            letter_spacing: 0.,
            line_height: 0.,
            decoration: 0,
        }
    }
}
#[derive(Clone, Copy, Debug, PartialEq, Default)]
pub struct LayoutBehavior {
    pub width_percent: Option<f32>,
    pub height_percent: Option<f32>,
    pub aspect_ratio: Option<f32>,
    pub position: u8,
    pub visibility: u8,
    pub insets: [Option<f32>; 4],
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct GridTrack {
    pub unit: u8,
    pub value: f32,
}
#[derive(Clone, Debug, PartialEq)]
pub struct AdvancedLayout {
    pub flow: u8,
    pub align_content: u8,
    pub reverse: bool,
    pub grid: bool,
    pub row_gap: Option<f32>,
    pub column_gap: Option<f32>,
    pub logical: [Option<f32>; 6],
    pub column: u16,
    pub column_span: u16,
    pub row: u16,
    pub row_span: u16,
    pub columns: Vec<GridTrack>,
    pub rows: Vec<GridTrack>,
}
impl Default for AdvancedLayout {
    fn default() -> Self {
        Self {
            flow: 0,
            align_content: 0,
            reverse: false,
            grid: false,
            row_gap: None,
            column_gap: None,
            logical: [None; 6],
            column: 0,
            column_span: 1,
            row: 0,
            row_span: 1,
            columns: vec![],
            rows: vec![],
        }
    }
}
#[derive(Clone, Debug, PartialEq)]
pub struct Gradient {
    pub kind: u8,
    pub start: [f32; 2],
    pub end: [f32; 2],
    pub stops: Vec<(f32, [u8; 4])>,
}
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct BorderSide {
    pub width: f32,
    pub color: [u8; 4],
    pub style: u8,
}
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct BoxDecoration {
    pub corners: Option<[f32; 4]>,
    pub borders: [Option<BorderSide>; 4],
}

#[derive(Clone, Debug, PartialEq)]
pub struct TextRun {
    pub text: String,
    pub mask: u8,
    pub font_size: f32,
    pub foreground: [u8; 4],
    pub weight: u16,
    pub italic: bool,
    pub family: String,
    pub decoration: u8,
}
#[derive(Debug)]
pub(crate) enum Command<'a> {
    Create(u32, Kind),
    Text(u32, &'a str),
    Style(u32, Style),
    Children(u32, Vec<u32>),
    Root(u32),
    Delete(u32),
    Viewport([f32; 2]),
    Layout(u32, LayoutStyle),
    Control(u32, ControlProperties),
    Image(u32, &'a [u8]),
    Header(u32, HeaderOptions),
    BottomNavigation(u32, BottomNavigation),
    DesktopNavigation(u32, DesktopNavigation),
    TextInput(u32, TextInputOptions),
    ScrollPosition(u32, u32, f32, bool),
    ObserveLayout(u32, u32),
    VirtualList(u32),
    Scroll(u32, bool),
    HeaderOpacity(u32, f32),
    FlexShrink(u32, f32),
    ScrollAxis(u32, u8),
    ExtendedLayout(u32, ExtendedLayout),
    Visual(u32, VisualStyle),
    Projection(u32, [f32; 16]),
    Typography(u32, TypographyStyle),
    LayoutBehavior(u32, LayoutBehavior),
    AdvancedLayout(u32, AdvancedLayout),
    BoxDecoration(u32, BoxDecoration),
    Enabled(u32, bool),
    TextRuns(u32, Vec<TextRun>),
    FontVariations(u32, Vec<([u8; 4], f32)>),
    FontSource(u32, Option<crate::font_assets::FontAsset>),
    RegisterFont(u32, crate::font_assets::FontAsset),
    FontReference(u32, u32),
    BackgroundImage(u32, u8, &'a [u8]),
    ImageTint(u32, Option<[u8; 4]>),
    ImageFit(u32, u8),
    ZIndex(u32, i32),
    Gradient(u32, Option<Gradient>),
    NavigationTransition(u32, u8, f32, u8),
    PlatformView(u32, &'a str),
}
struct Reader<'a> {
    bytes: &'a [u8],
    cursor: usize,
}
impl<'a> Reader<'a> {
    fn new(bytes: &'a [u8]) -> Self {
        Self { bytes, cursor: 0 }
    }
    fn take(&mut self, count: usize) -> Result<&'a [u8]> {
        let end = self
            .cursor
            .checked_add(count)
            .ok_or(Error::Wire("length overflow"))?;
        let value = self
            .bytes
            .get(self.cursor..end)
            .ok_or(Error::Wire("truncated field"))?;
        self.cursor = end;
        Ok(value)
    }
    fn u8(&mut self) -> Result<u8> {
        Ok(self.take(1)?[0])
    }
    fn u16(&mut self) -> Result<u16> {
        Ok(u16::from_le_bytes(self.take(2)?.try_into().unwrap()))
    }
    fn u32(&mut self) -> Result<u32> {
        Ok(u32::from_le_bytes(self.take(4)?.try_into().unwrap()))
    }
    fn id(&mut self) -> Result<u32> {
        let id = self.u32()?;
        if id == 0 {
            return Err(Error::Wire("zero node ID"));
        }
        Ok(id)
    }
    fn distance(&mut self) -> Result<f32> {
        let value = f32::from_le_bytes(self.take(4)?.try_into().unwrap());
        if !value.is_finite() || value < 0. {
            return Err(Error::Wire("invalid distance"));
        }
        Ok(value)
    }
    fn finite(&mut self) -> Result<f32> {
        let value = f32::from_le_bytes(self.take(4)?.try_into().unwrap());
        if !value.is_finite() {
            return Err(Error::Wire("nonfinite layout value"));
        }
        Ok(value)
    }
    fn dimension(&mut self) -> Result<Option<f32>> {
        let value = self.finite()?;
        if value == -1. {
            Ok(None)
        } else if value >= 0. {
            Ok(Some(value))
        } else {
            Err(Error::Wire("invalid dimension"))
        }
    }
    fn done(&self) -> Result<()> {
        if self.cursor == self.bytes.len() {
            Ok(())
        } else {
            Err(Error::Wire("trailing bytes"))
        }
    }
}
pub(crate) fn parse(bytes: &[u8]) -> Result<Vec<Command<'_>>> {
    if bytes.len() > MAX_BYTES {
        return Err(Error::Wire("batch exceeds 16 MiB"));
    }
    let mut reader = Reader::new(bytes);
    if reader.take(4)? != b"DNTV" || reader.u16()? != 1 || reader.u16()? != 16 {
        return Err(Error::Wire("unsupported header"));
    }
    if reader.u32()? as usize != bytes.len() {
        return Err(Error::Wire("batch length mismatch"));
    }
    let count = reader.u32()? as usize;
    // Smallest legal record is 9 bytes. Bound allocations by actual input.
    if count > bytes.len().saturating_sub(16) / 9 {
        return Err(Error::Wire("invalid command count"));
    }
    let mut commands = Vec::with_capacity(count);
    for _ in 0..count {
        let opcode = reader.u8()?;
        let payload_len = reader.u32()? as usize;
        let mut payload = Reader::new(reader.take(payload_len)?);
        let command = match opcode {
            1 => {
                let id = payload.id()?;
                let kind = match payload.u8()? {
                    1 => Kind::VStack,
                    2 => Kind::Text,
                    3 => Kind::Button,
                    4 => Kind::ScrollView,
                    5 => Kind::TextField,
                    6 => Kind::Switch,
                    7 => Kind::Slider,
                    8 => Kind::Image,
                    9 => Kind::Icon,
                    10 => Kind::Navigation,
                    11 => Kind::NavigationPage,
                    12 => Kind::RadioButton,
                    13 => Kind::NavigationHeader,
                    14 => Kind::BottomNavigationBar,
                    15 => Kind::DesktopNavigation,
                    16 => Kind::PlatformView,
                    _ => return Err(Error::Wire("unknown node kind")),
                };
                Command::Create(id, kind)
            }
            2 => {
                let id = payload.id()?;
                let count = payload.u32()? as usize;
                let text = std::str::from_utf8(payload.take(count)?)
                    .map_err(|_| Error::Wire("invalid UTF-8"))?;
                Command::Text(id, text)
            }
            3 => {
                let id = payload.id()?;
                let style = Style {
                    padding: payload.distance()?,
                    background: payload.take(4)?.try_into().unwrap(),
                    radius: payload.distance()?,
                    foreground: payload.take(4)?.try_into().unwrap(),
                    font_size: payload.distance()?,
                };
                if style.font_size == 0. {
                    return Err(Error::Wire("zero font size"));
                }
                Command::Style(id, style)
            }
            4 => {
                let id = payload.id()?;
                let count = payload.u32()? as usize;
                if count != payload_len.saturating_sub(8) / 4 {
                    return Err(Error::Wire("child count mismatch"));
                }
                let mut children = Vec::with_capacity(count);
                for _ in 0..count {
                    children.push(payload.id()?);
                }
                Command::Children(id, children)
            }
            5 => Command::Root(payload.u32()?),
            6 => Command::Delete(payload.id()?),
            7 => Command::Viewport([payload.distance()?, payload.distance()?]),
            8 => {
                let id = payload.id()?;
                let dimension = |payload: &mut Reader<'_>| -> Result<Option<f32>> {
                    let value = f32::from_le_bytes(payload.take(4)?.try_into().unwrap());
                    if value == -1. {
                        Ok(None)
                    } else if value.is_finite() && value >= 0. {
                        Ok(Some(value))
                    } else {
                        Err(Error::Wire("invalid dimension"))
                    }
                };
                let layout = LayoutStyle {
                    width: dimension(&mut payload)?,
                    height: dimension(&mut payload)?,
                    gap: payload.distance()?,
                    grow: payload.distance()?,
                    shrink: 0.,
                    direction: payload.u8()?,
                    main_alignment: payload.u8()?,
                    cross_alignment: payload.u8()?,
                };
                if layout.direction > 1
                    || layout.main_alignment > 5
                    || layout.cross_alignment > 5
                    || payload.u8()? != 0
                {
                    return Err(Error::Wire("invalid layout flags"));
                }
                Command::Layout(id, layout)
            }
            9 => {
                let id = payload.id()?;
                let finite = |payload: &mut Reader<'_>| -> Result<f32> {
                    let value = f32::from_le_bytes(payload.take(4)?.try_into().unwrap());
                    if value.is_finite() {
                        Ok(value)
                    } else {
                        Err(Error::Wire("nonfinite control value"))
                    }
                };
                let value = finite(&mut payload)?;
                let minimum = finite(&mut payload)?;
                let maximum = finite(&mut payload)?;
                let focus_revision = payload.u32()?;
                let focus = payload.u32()?;
                let placeholder_len = payload.u32()? as usize;
                if minimum >= maximum || value < minimum || value > maximum || focus > 1 {
                    return Err(Error::Wire("invalid control range"));
                }
                let placeholder = std::str::from_utf8(payload.take(placeholder_len)?)
                    .map_err(|_| Error::Wire("invalid placeholder UTF8"))?;
                Command::Control(
                    id,
                    ControlProperties {
                        value,
                        minimum,
                        maximum,
                        focus_revision,
                        focus_requested: focus == 1,
                        placeholder: std::sync::Arc::from(placeholder),
                    },
                )
            }
            10 => {
                let id = payload.id()?;
                let image_len = payload.u32()? as usize;
                if image_len == 0 || image_len > 8 * 1024 * 1024 {
                    return Err(Error::Wire("invalid image length"));
                }
                Command::Image(id, payload.take(image_len)?)
            }
            11 => {
                let id = payload.id()?;
                let flags = payload.u32()?;
                let leading = payload.u32()? as usize;
                let trailing = payload.u32()? as usize;
                if flags > 15 || leading > 32 || trailing > 32 || leading + trailing > 32 {
                    return Err(Error::Wire("invalid header options"));
                }
                let mut actions = Vec::with_capacity(leading + trailing);
                for _ in 0..leading + trailing {
                    let title_len = payload.u32()? as usize;
                    let title = std::str::from_utf8(payload.take(title_len)?)
                        .map_err(|_| Error::Wire("invalid header UTF8"))?;
                    if title.trim().is_empty() {
                        return Err(Error::Wire("empty header action"));
                    }
                    actions.push(std::sync::Arc::from(title));
                }
                Command::Header(
                    id,
                    HeaderOptions {
                        visible: flags & 1 != 0,
                        back_visible: flags & 2 != 0,
                        can_pop: flags & 4 != 0,
                        large_title: flags & 8 != 0,
                        opacity: 1.,
                        leading,
                        actions,
                    },
                )
            }
            12 => {
                let id = payload.id()?;
                let mode = payload.u8()?;
                if mode > 1 {
                    return Err(Error::Wire("invalid nested scroll mode"));
                }
                Command::Scroll(id, mode == 1)
            }
            13 => {
                let id = payload.id()?;
                let opacity = payload.distance()?;
                if opacity > 1. {
                    return Err(Error::Wire("invalid header opacity"));
                }
                Command::HeaderOpacity(id, opacity)
            }
            14 => Command::FlexShrink(payload.id()?, payload.distance()?),
            15 => {
                let id = payload.id()?;
                let axis = payload.u8()?;
                if axis > 1 {
                    return Err(Error::Wire("invalid scroll axis"));
                }
                Command::ScrollAxis(id, axis)
            }
            16 => {
                let id = payload.id()?;
                let layout = ExtendedLayout {
                    margin: [
                        payload.finite()?,
                        payload.finite()?,
                        payload.finite()?,
                        payload.finite()?,
                    ],
                    min_width: payload.dimension()?,
                    min_height: payload.dimension()?,
                    max_width: payload.dimension()?,
                    max_height: payload.dimension()?,
                    basis: payload.dimension()?,
                    wrap: payload.u8()?,
                    align_self: payload.u8()?,
                    padding: [
                        payload.dimension()?,
                        payload.dimension()?,
                        payload.dimension()?,
                        payload.dimension()?,
                    ],
                };
                if layout.wrap > 2
                    || layout.align_self > 4
                    || layout
                        .padding
                        .iter()
                        .any(|v| v.is_some() != layout.padding[0].is_some())
                    || matches!((layout.min_width, layout.max_width), (Some(min), Some(max)) if min > max)
                    || matches!((layout.min_height, layout.max_height), (Some(min), Some(max)) if min > max)
                {
                    return Err(Error::Wire("invalid extended layout"));
                }
                Command::ExtendedLayout(id, layout)
            }
            17 => {
                let id = payload.id()?;
                let visual = VisualStyle {
                    opacity: payload.distance()?,
                    border_width: payload.distance()?,
                    border_color: payload.take(4)?.try_into().unwrap(),
                    shadow_color: payload.take(4)?.try_into().unwrap(),
                    shadow_blur: payload.distance()?,
                    shadow_x: payload.finite()?,
                    shadow_y: payload.finite()?,
                    translate_x: payload.finite()?,
                    translate_y: payload.finite()?,
                    scale_x: payload.finite()?,
                    scale_y: payload.finite()?,
                    rotation: payload.finite()?,
                    clip: match payload.u8()? {
                        0 => false,
                        1 => true,
                        _ => return Err(Error::Wire("invalid clipping")),
                    },
                };
                if visual.opacity > 1. {
                    return Err(Error::Wire("invalid opacity"));
                }
                Command::Visual(id, visual)
            }
            18 => {
                let id = payload.id()?;
                let weight = payload.u16()?;
                let italic = payload.u8()?;
                let alignment = payload.u8()?;
                let max_lines = payload.u32()? as i32;
                let overflow = payload.u8()?;
                let letter_spacing = payload.finite()?;
                let line_height = payload.distance()?;
                let decoration = payload.u8()?;
                let count = payload.u32()? as usize;
                if decoration > 3
                    || count > 4096
                    || !(100..=900).contains(&weight)
                    || weight % 100 != 0
                    || italic > 1
                    || alignment > 5
                    || max_lines < -1
                    || overflow > 5
                {
                    return Err(Error::Wire("invalid typography"));
                }
                let family = std::str::from_utf8(payload.take(count)?)
                    .map_err(|_| Error::Wire("invalid font UTF8"))?;
                if family.contains('\0') {
                    return Err(Error::Wire("invalid font family"));
                }
                Command::Typography(
                    id,
                    TypographyStyle {
                        family: std::sync::Arc::from(family),
                        weight,
                        italic: italic == 1,
                        alignment,
                        max_lines,
                        overflow,
                        letter_spacing,
                        line_height,
                        decoration,
                    },
                )
            }
            19 => {
                let id = payload.id()?;
                let width_percent = payload.dimension()?;
                let height_percent = payload.dimension()?;
                let aspect_ratio = payload.dimension()?;
                let position = payload.u8()?;
                let visibility = payload.u8()?;
                let mask = payload.u8()?;
                if position > 1
                    || visibility > 2
                    || mask > 15
                    || payload.u8()? != 0
                    || aspect_ratio == Some(0.)
                {
                    return Err(Error::Wire("invalid layout behavior"));
                }
                let mut insets = [None; 4];
                for (i, inset) in insets.iter_mut().enumerate() {
                    let value = payload.finite()?;
                    if mask & (1 << i) != 0 {
                        *inset = Some(value);
                    }
                }
                Command::LayoutBehavior(
                    id,
                    LayoutBehavior {
                        width_percent,
                        height_percent,
                        aspect_ratio,
                        position,
                        visibility,
                        insets,
                    },
                )
            }
            20 => {
                let id = payload.id()?;
                let flow = payload.u8()?;
                let align_content = payload.u8()?;
                let reverse = payload.u8()?;
                let grid = payload.u8()?;
                let row_gap = payload.dimension()?;
                let column_gap = payload.dimension()?;
                let mut values = [0.; 6];
                for v in &mut values {
                    *v = payload.finite()?;
                }
                let mask = payload.u8()?;
                if flow > 2
                    || align_content > 7
                    || reverse > 1
                    || grid > 1
                    || mask > 63
                    || payload.u8()? != 0
                {
                    return Err(Error::Wire("invalid advanced layout flags"));
                }
                let mut logical = [None; 6];
                for i in 0..6 {
                    if mask & (1 << i) != 0 {
                        logical[i] = Some(values[i]);
                    }
                }
                if logical[2].is_some_and(|v| v < 0.) || logical[3].is_some_and(|v| v < 0.) {
                    return Err(Error::Wire("negative logical padding"));
                }
                let column = payload.u16()?;
                let column_span = payload.u16()?;
                let row = payload.u16()?;
                let row_span = payload.u16()?;
                let columns = payload.u16()? as usize;
                let rows = payload.u16()? as usize;
                if column > 128
                    || row > 128
                    || !(1..=128).contains(&column_span)
                    || !(1..=128).contains(&row_span)
                    || column as u32 + column_span as u32 > 129
                    || row as u32 + row_span as u32 > 129
                    || columns > 128
                    || rows > 128
                    || (grid == 0 && columns + rows != 0)
                    || payload.u16()? != 0
                {
                    return Err(Error::Wire("invalid grid bounds"));
                }
                let mut tracks = Vec::with_capacity(columns + rows);
                for _ in 0..columns + rows {
                    let unit = payload.u8()?;
                    let value = payload.distance()?;
                    if unit > 3 || (unit == 0 && value != 0.) || (unit == 3 && value == 0.) {
                        return Err(Error::Wire("invalid grid track"));
                    }
                    tracks.push(GridTrack { unit, value });
                }
                let row_tracks = tracks.split_off(columns);
                Command::AdvancedLayout(
                    id,
                    AdvancedLayout {
                        flow,
                        align_content,
                        reverse: reverse != 0,
                        grid: grid != 0,
                        row_gap,
                        column_gap,
                        logical,
                        column,
                        column_span,
                        row,
                        row_span,
                        columns: tracks,
                        rows: row_tracks,
                    },
                )
            }
            39 => {
                let key = payload.id()?;
                let family_size = payload.u32()? as usize;
                if family_size > 4096 {
                    return Err(Error::Wire("font family too long"));
                }
                let family = std::str::from_utf8(payload.take(family_size)?)
                    .map_err(|_| Error::Wire("font family UTF-8"))?;
                let size = payload.u32()? as usize;
                if size > 8 * 1024 * 1024 {
                    return Err(Error::Wire("font asset too large"));
                }
                Command::RegisterFont(
                    key,
                    crate::font_assets::FontAsset::new(family, payload.take(size)?)?,
                )
            }
            40 => Command::FontReference(payload.id()?, payload.id()?),
            30 => {
                let id = payload.id()?;
                let family_size = payload.u32()? as usize;
                if family_size > 4096 {
                    return Err(Error::Wire("font family too long"));
                }
                let family = std::str::from_utf8(payload.take(family_size)?)
                    .map_err(|_| Error::Wire("font family UTF-8"))?;
                let size = payload.u32()? as usize;
                if size > 8 * 1024 * 1024 {
                    return Err(Error::Wire("font asset too large"));
                }
                let data = payload.take(size)?;
                let asset = if size == 0 {
                    if !family.is_empty() {
                        return Err(Error::Wire("font reset family must be empty"));
                    }
                    None
                } else {
                    Some(crate::font_assets::FontAsset::new(family, data)?)
                };
                Command::FontSource(id, asset)
            }
            29 => {
                let id = payload.id()?;
                let count = payload.u8()?;
                if count > 16 {
                    return Err(Error::Wire("too many font axes"));
                }
                let mut axes = Vec::with_capacity(count as usize);
                for _ in 0..count {
                    let tag: [u8; 4] = payload.take(4)?.try_into().unwrap();
                    let value = f32::from_le_bytes(payload.take(4)?.try_into().unwrap());
                    if !tag.iter().all(u8::is_ascii_alphanumeric)
                        || !value.is_finite()
                        || axes.iter().any(|(old, _)| *old == tag)
                    {
                        return Err(Error::Wire("invalid font axis"));
                    }
                    axes.push((tag, value));
                }
                axes.sort_by_key(|(tag, _)| *tag);
                Command::FontVariations(id, axes)
            }
            28 => {
                let id = payload.id()?;
                let count = payload.u32()? as usize;
                if count > 256 {
                    return Err(Error::Wire("too many text runs"));
                }
                let mut runs = Vec::with_capacity(count);
                for _ in 0..count {
                    let count = payload.u32()? as usize;
                    let text = std::str::from_utf8(payload.take(count)?)
                        .map_err(|_| Error::Wire("invalid run UTF-8"))?
                        .to_owned();
                    let mask = payload.u8()?;
                    let font_size = payload.distance()?;
                    let foreground = payload.take(4)?.try_into().unwrap();
                    let weight = u16::from_le_bytes(payload.take(2)?.try_into().unwrap());
                    let italic = payload.u8()?;
                    let decoration = payload.u8()?;
                    let count = payload.u32()? as usize;
                    if count > 4096 {
                        return Err(Error::Wire("run font family too long"));
                    }
                    let family = std::str::from_utf8(payload.take(count)?)
                        .map_err(|_| Error::Wire("invalid run family"))?
                        .to_owned();
                    if mask > 63
                        || font_size == 0.
                        || !(100..=900).contains(&weight)
                        || weight % 100 != 0
                        || italic > 1
                        || decoration > 3
                        || family.contains('\0')
                    {
                        return Err(Error::Wire("invalid text run style"));
                    }
                    runs.push(TextRun {
                        text,
                        mask,
                        font_size,
                        foreground,
                        weight,
                        italic: italic != 0,
                        family,
                        decoration,
                    });
                }
                Command::TextRuns(id, runs)
            }
            27 => {
                let id = payload.id()?;
                let enabled = payload.u8()?;
                if enabled > 1 {
                    return Err(Error::Wire("invalid enabled flag"));
                }
                Command::Enabled(id, enabled != 0)
            }
            26 => {
                let id = payload.id()?;
                let mask = payload.u8()?;
                if mask > 31 {
                    return Err(Error::Wire("invalid box decoration mask"));
                }
                let mut radii = [0.; 4];
                for r in &mut radii {
                    *r = payload.distance()?;
                }
                let mut borders = [None; 4];
                for (i, border) in borders.iter_mut().enumerate() {
                    let width = payload.distance()?;
                    let color = payload.take(4)?.try_into().unwrap();
                    let style = payload.u8()?;
                    if style > 2 {
                        return Err(Error::Wire("invalid border line style"));
                    }
                    if mask & (2 << i) != 0 {
                        *border = Some(BorderSide {
                            width,
                            color,
                            style,
                        });
                    }
                }
                Command::BoxDecoration(
                    id,
                    BoxDecoration {
                        corners: (mask & 1 != 0).then_some(radii),
                        borders,
                    },
                )
            }
            25 => {
                let id = payload.id()?;
                let fit = payload.u8()?;
                let length = payload.u32()? as usize;
                if fit > 4 || length > 8 * 1024 * 1024 {
                    return Err(Error::Wire("invalid background image"));
                }
                Command::BackgroundImage(id, fit, payload.take(length)?)
            }
            24 => {
                let id = payload.id()?;
                let enabled = payload.u8()?;
                if enabled > 1 {
                    return Err(Error::Wire("invalid image tint flag"));
                }
                let color: [u8; 4] = payload.take(4)?.try_into().unwrap();
                Command::ImageTint(id, (enabled == 1).then_some(color))
            }
            23 => {
                let id = payload.id()?;
                let fit = payload.u8()?;
                if fit > 4 {
                    return Err(Error::Wire("invalid image fit"));
                }
                Command::ImageFit(id, fit)
            }
            22 => Command::ZIndex(payload.id()?, payload.u32()? as i32),
            21 => {
                let id = payload.id()?;
                let kind = payload.u8()?;
                let count = payload.u8()? as usize;
                let start = [payload.distance()?, payload.distance()?];
                let end = [payload.distance()?, payload.distance()?];
                if kind > 3
                    || (kind == 0 && count != 0)
                    || (kind != 0 && !(2..=64).contains(&count))
                    || start.iter().chain(end.iter()).any(|v| *v > 1.)
                    || (kind == 1 && start == end)
                    || (kind == 2 && end[0] == 0.)
                {
                    return Err(Error::Wire("invalid gradient"));
                }
                let mut stops: Vec<(f32, [u8; 4])> = Vec::with_capacity(count);
                for i in 0..count {
                    let offset = payload.distance()?;
                    let color = payload.take(4)?.try_into().unwrap();
                    if offset > 1.
                        || (i == 0 && offset != 0.)
                        || (i > 0 && stops[i - 1].0 >= offset)
                        || (i == count - 1 && offset != 1.)
                    {
                        return Err(Error::Wire("invalid gradient stops"));
                    }
                    stops.push((offset, color));
                }
                Command::Gradient(
                    id,
                    if kind == 0 {
                        None
                    } else {
                        Some(Gradient {
                            kind,
                            start,
                            end,
                            stops,
                        })
                    },
                )
            }
            33 => {
                let id = payload.id()?;
                let selected = payload.u32()? as i32;
                let open = payload.u32()?;
                let width = payload.distance()?;
                let count = payload.u32()? as usize;
                let theme = payload.u32()?;
                if theme > 2
                    || count > 256
                    || selected < -1
                    || selected >= count as i32
                    || open > 1
                    || !(180. ..=1000.).contains(&width)
                {
                    return Err(Error::Wire("invalid desktop navigation"));
                }
                let mut strings = Vec::new();
                for _ in 0..1 + count * 2 {
                    let len = payload.u32()? as usize;
                    let value = std::str::from_utf8(payload.take(len)?)
                        .map_err(|_| Error::Wire("invalid sidebar UTF8"))?;
                    strings.push(std::sync::Arc::<str>::from(value));
                }
                let title = strings.remove(0);
                let items = strings
                    .chunks_exact(2)
                    .map(|v| (v[0].clone(), v[1].clone()))
                    .collect();
                Command::DesktopNavigation(
                    id,
                    DesktopNavigation {
                        theme,
                        selected,
                        open: open != 0,
                        width,
                        title,
                        items,
                    },
                )
            }
            37 => Command::VirtualList(payload.id()?),
            36 => Command::ObserveLayout(payload.id()?, payload.u32()?),
            35 => {
                let id = payload.id()?;
                let revision = payload.u32()?;
                let offset = payload.distance()?;
                let observe = payload.u8()?;
                if observe > 1 {
                    return Err(Error::Wire("invalid scroll observation"));
                }
                Command::ScrollPosition(id, revision, offset, observe != 0)
            }
            34 => {
                let id = payload.id()?;
                let input = TextInputOptions {
                    keyboard: payload.u8()?,
                    action: payload.u8()?,
                    capitalization: payload.u8()?,
                    autofill: payload.u8()?,
                    flags: payload.u8()?,
                    placeholder_color: if payload.cursor == payload.bytes.len() {
                        None
                    } else {
                        Some(payload.take(4)?.try_into().unwrap())
                    },
                };
                if input.keyboard > 5
                    || input.action > 5
                    || input.capitalization > 3
                    || input.autofill > 5
                    || input.flags > 7
                    || (input.password() || input.keyboard != 0) && input.multiline()
                {
                    return Err(Error::Wire("invalid text input options"));
                }
                Command::TextInput(id, input)
            }
            32 => {
                let id = payload.id()?;
                let selected = payload.u32()? as usize;
                let count = payload.u32()? as usize;
                if !(2..=5).contains(&count) || selected >= count {
                    return Err(Error::Wire("invalid bottom navigation selection/count"));
                }
                let mut items = Vec::with_capacity(count);
                for _ in 0..count {
                    let icon = payload.u32()?;
                    let len = payload.u32()? as usize;
                    let label = std::str::from_utf8(payload.take(len)?)
                        .map_err(|_| Error::Wire("invalid tab UTF8"))?;
                    if icon > 4 || label.trim().is_empty() {
                        return Err(Error::Wire("invalid tab item"));
                    }
                    items.push((icon, std::sync::Arc::from(label)));
                }
                Command::BottomNavigation(id, BottomNavigation { selected, items })
            }
            31 => {
                let id = payload.id()?;
                let kind = payload.u8()?;
                let duration = payload.distance()?;
                let easing = payload.u8()?;
                if kind > 3
                    || easing > 3
                    || !duration.is_finite()
                    || !(0. ..=10.).contains(&duration)
                    || (kind != 3 && duration == 0.)
                {
                    return Err(Error::Wire("invalid navigation transition"));
                }
                Command::NavigationTransition(id, kind, duration, easing)
            }
            38 => {
                let id = payload.id()?;
                let mut matrix = [0.; 16];
                for value in &mut matrix {
                    *value = payload.finite()?;
                }
                Command::Projection(id, matrix)
            }
            41 => {
                let id = payload.id()?;
                let count = payload.u32()? as usize;
                let name = std::str::from_utf8(payload.take(count)?)
                    .map_err(|_| Error::Wire("invalid UTF-8"))?;
                if !crate::platform_view::valid_type(name) {
                    return Err(Error::Wire("invalid platform view type"));
                }
                Command::PlatformView(id, name)
            }
            _ => return Err(Error::Wire("unknown opcode")),
        };
        payload.done()?;
        commands.push(command);
    }
    reader.done()?;
    Ok(commands)
}
