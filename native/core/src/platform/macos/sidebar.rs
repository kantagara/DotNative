//! AppKit split-view sidebar: native material, list selection and keyboard navigation.
use super::*;
use objc2::runtime::ProtocolObject;
use objc2_app_kit::{
    NSAppearanceCustomization, NSBezelStyle, NSControlTextEditingDelegate, NSSplitViewController,
    NSSplitViewItem, NSTableColumn, NSTableView, NSTableViewDataSource, NSTableViewDelegate,
    NSTableViewStyle, NSViewController,
};
use objc2_foundation::{NSIndexSet, NSInteger, NSObjectProtocol};

pub(super) struct ListState {
    id: u32,
    data: RefCell<crate::protocol::DesktopNavigation>,
    updating: Cell<bool>,
}
define_class!(
    #[unsafe(super(NSTableView))]
    #[thread_kind=MainThreadOnly]
    #[name = "DotNativeSidebarList"]
    #[ivars=ListState]
    pub(super) struct SidebarList;
    unsafe impl NSObjectProtocol for SidebarList {}
    unsafe impl NSControlTextEditingDelegate for SidebarList {}
    unsafe impl NSTableViewDataSource for SidebarList {
        #[unsafe(method(numberOfRowsInTableView:))]
        fn count(&self, _table: &NSTableView) -> NSInteger {
            self.ivars().data.borrow().items.len() as NSInteger
        }
    }
    unsafe impl NSTableViewDelegate for SidebarList {
        #[unsafe(method_id(tableView:viewForTableColumn:row:))]
        fn cell(
            &self,
            _table: &NSTableView,
            _column: Option<&NSTableColumn>,
            row: NSInteger,
        ) -> Option<Retained<NSView>> {
            self.make_cell(row)
        }
        #[unsafe(method(tableViewSelectionDidChange:))]
        fn selected(&self, _notification: &NSNotification) {
            if !self.ivars().updating.get() && self.selectedRow() >= 0 {
                crate::events::emit(self.ivars().id, 3, self.selectedRow() as f32, String::new());
            }
        }
    }
);
impl SidebarList {
    fn make_cell(&self, row: NSInteger) -> Option<Retained<NSView>> {
        let data = self.ivars().data.borrow();
        let (title, icon) = data.items.get(usize::try_from(row).ok()?)?;
        let cell = objc2_app_kit::NSTableCellView::initWithFrame(
            objc2_app_kit::NSTableCellView::alloc(self.mtm()),
            CGRect::new(CGPoint::ZERO, CGSize::new(220., 28.)),
        );
        let text = NSTextField::labelWithString(&NSString::from_str(title), self.mtm());
        text.setFont(Some(&NSFont::systemFontOfSize(13.)));
        text.setTextColor(Some(&NSColor::labelColor()));
        let text_height = text.fittingSize().height;
        text.setFrame(CGRect::new(
            CGPoint::new(30., (28. - text_height) / 2.),
            CGSize::new(185., text_height),
        ));
        text.setAutoresizingMask(objc2_app_kit::NSAutoresizingMaskOptions::ViewWidthSizable);
        cell.addSubview(&text);
        unsafe {
            cell.setTextField(Some(&text));
        }
        if let Some(symbol) = icon.strip_prefix("sf:") {
            if let Some(image) = NSImage::imageWithSystemSymbolName_accessibilityDescription(
                &NSString::from_str(symbol),
                None,
            ) {
                let configuration =
                    objc2_app_kit::NSImageSymbolConfiguration::configurationWithPointSize_weight(
                        13., 0.,
                    );
                let image = image
                    .imageWithSymbolConfiguration(&configuration)
                    .unwrap_or(image);
                // Symbols include unequal transparent margins. Align their optical
                // rectangle with the text's cap height, not their bitmap frame.
                let optical = image.alignmentRect();
                let font = NSFont::systemFontOfSize(13.);
                let center_y = text.frame().origin.y
                    + text.lastBaselineOffsetFromBottom()
                    + font.capHeight() / 2.
                    + 0.5;
                let view = NSImageView::initWithFrame(
                    NSImageView::alloc(self.mtm()),
                    CGRect::new(
                        CGPoint::new(
                            13. - optical.origin.x - optical.size.width / 2.,
                            center_y - optical.origin.y - optical.size.height / 2.,
                        ),
                        image.size(),
                    ),
                );
                view.setImageScaling(objc2_app_kit::NSImageScaling::ScaleNone);
                view.setImage(Some(&image));
                cell.addSubview(&view);
                unsafe {
                    cell.setImageView(Some(&view));
                }
            }
        } else {
            let glyph = NSTextField::labelWithString(&NSString::from_str(icon), self.mtm());
            glyph.setFrame(CGRect::new(CGPoint::new(4., 5.), CGSize::new(22., 20.)));
            cell.addSubview(&glyph);
        }
        Some(Retained::into_super(cell))
    }
    fn make(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(ListState {
            id,
            data: RefCell::default(),
            updating: Cell::new(false),
        });
        let view: Retained<Self> =
            unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] };
        unsafe {
            view.setDataSource(Some(ProtocolObject::from_ref(&*view)));
            view.setDelegate(Some(ProtocolObject::from_ref(&*view)));
        }
        view.setStyle(NSTableViewStyle::SourceList);
        view.setHeaderView(None);
        view.setRowHeight(28.);
        view.setAllowsMultipleSelection(false);
        view.setAllowsEmptySelection(true);
        let column = NSTableColumn::initWithIdentifier(
            NSTableColumn::alloc(mtm),
            &NSString::from_str("navigation"),
        );
        column.setWidth(240.);
        view.addTableColumn(&column);
        view
    }
}
pub(super) struct Sidebar {
    pub controller: Retained<NSSplitViewController>,
    pub content: Retained<NSView>,
    pane: Retained<NSView>,
    item: Retained<NSSplitViewItem>,
    list: Retained<SidebarList>,
    scroll: Retained<NSScrollView>,
    title: Retained<NSTextField>,
    toggle: Retained<super::toolbar::ToolbarButton>,
}
impl Sidebar {
    pub fn new(id: u32, mtm: MainThreadMarker) -> Self {
        let controller = NSSplitViewController::new(mtm);
        let pane = FlippedView::make(mtm);
        let content = FlippedView::make(mtm);
        let sidebar_vc = NSViewController::new(mtm);
        sidebar_vc.setView(&pane);
        let body_vc = NSViewController::new(mtm);
        body_vc.setView(&content);
        let item = NSSplitViewItem::sidebarWithViewController(&sidebar_vc);
        item.setCanCollapse(false);
        controller.addSplitViewItem(&item);
        controller.addSplitViewItem(&NSSplitViewItem::splitViewItemWithViewController(&body_vc));
        controller.splitView().setVertical(true);
        let list = SidebarList::make(id, mtm);
        let scroll = NSScrollView::initWithFrame(NSScrollView::alloc(mtm), CGRect::ZERO);
        scroll.setDrawsBackground(false);
        scroll.setHasVerticalScroller(true);
        scroll.setDocumentView(Some(&list));
        let title = NSTextField::labelWithString(&NSString::from_str(""), mtm);
        title.setTextColor(Some(&NSColor::secondaryLabelColor()));
        title.setFont(Some(&NSFont::systemFontOfSize(12.)));
        let toggle = super::toolbar::ToolbarButton::make(id, -1., "☰", mtm);
        toggle.setBezelStyle(NSBezelStyle::Toolbar);
        pane.addSubview(&scroll);
        pane.addSubview(&title);
        pane.addSubview(&toggle);
        Self {
            controller,
            content,
            pane,
            item,
            list,
            scroll,
            title,
            toggle,
        }
    }
    pub fn sync(&self, node: &Node, width: f64, height: f64) {
        let data = &node.desktop_navigation;
        let appearance = match data.theme {
            1 => objc2_app_kit::NSAppearance::appearanceNamed(unsafe {
                objc2_app_kit::NSAppearanceNameAqua
            }),
            2 => objc2_app_kit::NSAppearance::appearanceNamed(unsafe {
                objc2_app_kit::NSAppearanceNameDarkAqua
            }),
            _ => None,
        };
        self.controller.view().setAppearance(appearance.as_deref());
        let pane_width = if data.open { data.width as f64 } else { 51. };
        self.item.setMinimumThickness(pane_width);
        self.item.setMaximumThickness(pane_width);
        self.controller
            .view()
            .setFrame(CGRect::new(CGPoint::ZERO, CGSize::new(width, height)));
        self.controller
            .splitView()
            .setPosition_ofDividerAtIndex(pane_width, 0);
        self.pane.setNeedsLayout(true);
        self.scroll.setFrame(CGRect::new(
            CGPoint::new(8., 76.),
            CGSize::new((pane_width - 16.).max(0.), (height - 84.).max(0.)),
        ));
        self.scroll.setHidden(!data.open);
        self.title.setFrame(CGRect::new(
            CGPoint::new(14., 48.),
            CGSize::new((pane_width - 28.).max(0.), 22.),
        ));
        self.title.setHidden(!data.open);
        self.title.setStringValue(&NSString::from_str(&data.title));
        self.toggle
            .setFrame(CGRect::new(CGPoint::new(8., 8.), CGSize::new(34., 30.)));
        let list_width = self.scroll.contentSize().width;
        self.list.setFrameSize(CGSize::new(
            list_width,
            (data.items.len() as f64 * 30.).max(self.scroll.contentSize().height),
        ));
        if let Some(column) = self.list.tableColumns().firstObject() {
            column.setWidth((list_width - 6.).max(0.));
        }
        self.toggle.setEnabled(node.enabled);
        self.list.ivars().updating.set(true);
        let items_changed = self.list.ivars().data.borrow().items != data.items;
        *self.list.ivars().data.borrow_mut() = data.clone();
        if items_changed {
            self.list.reloadData();
        }
        if self.list.selectedRow() != data.selected as NSInteger {
            if data.selected >= 0 {
                self.list.selectRowIndexes_byExtendingSelection(
                    &NSIndexSet::indexSetWithIndex(data.selected as usize),
                    false,
                );
            } else {
                unsafe {
                    self.list.deselectAll(None);
                }
            }
        }
        self.list.setEnabled(node.enabled && data.open);
        self.list.ivars().updating.set(false);
    }
}
