//! AppKit owns window chrome, sizing and overflow. No Taffy row imitates a toolbar.
use crate::Node;
use objc2::{
    define_class, msg_send, rc::Retained, runtime::ProtocolObject, sel, DefinedClass,
    MainThreadMarker, MainThreadOnly, Message,
};
use objc2_app_kit::{
    NSBezelStyle, NSButton, NSFont, NSToolbar, NSToolbarDelegate, NSToolbarDisplayMode,
    NSToolbarFlexibleSpaceItemIdentifier, NSToolbarItem,
};
use objc2_foundation::{NSArray, NSObjectProtocol, NSString};

pub(super) struct Action {
    node: u32,
    index: f32,
}
define_class!(
    #[unsafe(super(NSButton))]
    #[thread_kind=MainThreadOnly]
    #[name="DotNativeMacToolbarAction"]
    #[ivars=Action]
    pub(super) struct ToolbarButton;
    impl ToolbarButton {
        #[unsafe(method(invoke:))]
        fn invoke(&self, _sender: &NSButton) { crate::events::emit(self.ivars().node, 3, self.ivars().index, String::new()); }
    }
);
impl ToolbarButton {
    pub(super) fn make(
        node: u32,
        index: f32,
        title: &str,
        mtm: MainThreadMarker,
    ) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(Action { node, index });
        let button: Retained<Self> = unsafe {
            msg_send![super(allocated), initWithFrame: objc2_core_foundation::CGRect::ZERO]
        };
        button.setTitle(&NSString::from_str(title));
        button.setFont(Some(&NSFont::systemFontOfSize(13.)));
        button.setBezelStyle(NSBezelStyle::Toolbar);
        button.setBordered(true);
        unsafe {
            button.setTarget(Some(&button));
            button.setAction(Some(sel!(invoke:)));
        }
        button.sizeToFit();
        button
    }
}
pub(super) struct Configuration {
    id: u32,
    node: Node,
}
define_class!(
    #[unsafe(super(NSToolbar))]
    #[thread_kind=MainThreadOnly]
    #[name = "DotNativeMacToolbar"]
    #[ivars=Configuration]
    pub(super) struct NativeToolbar;
    unsafe impl NSObjectProtocol for NativeToolbar {}
    unsafe impl NSToolbarDelegate for NativeToolbar {
        #[unsafe(method_id(toolbarDefaultItemIdentifiers:))]
        fn defaults(&self, _toolbar: &NSToolbar) -> Retained<NSArray<NSString>> {
            self.identifiers()
        }
        #[unsafe(method_id(toolbarAllowedItemIdentifiers:))]
        fn allowed(&self, _toolbar: &NSToolbar) -> Retained<NSArray<NSString>> {
            self.identifiers()
        }
        #[unsafe(method_id(toolbar:itemForItemIdentifier:willBeInsertedIntoToolbar:))]
        fn item(
            &self,
            _toolbar: &NSToolbar,
            identifier: &NSString,
            _insert: bool,
        ) -> Option<Retained<NSToolbarItem>> {
            self.make_item(identifier)
        }
    }
);
impl NativeToolbar {
    fn make_item(&self, identifier: &NSString) -> Option<Retained<NSToolbarItem>> {
        let key = identifier.to_string();
        let (index, title) = if key == "back" {
            (-1., "Back")
        } else {
            let index = key.strip_prefix("action-")?.parse::<usize>().ok()?;
            (
                index as f32,
                self.ivars().node.header.actions.get(index)?.as_ref(),
            )
        };
        let item =
            NSToolbarItem::initWithItemIdentifier(NSToolbarItem::alloc(self.mtm()), identifier);
        let button = ToolbarButton::make(self.ivars().id, index, title, self.mtm());
        item.setLabel(&NSString::from_str(title));
        item.setView(Some(&button));
        Some(item)
    }
    pub(super) fn make(id: u32, node: &Node, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(Configuration {
            id,
            node: node.clone(),
        });
        let toolbar: Retained<Self> = unsafe {
            msg_send![super(allocated), initWithIdentifier: &*NSString::from_str("DotNative.Screen")]
        };
        toolbar.setAllowsUserCustomization(false);
        toolbar.setAutosavesConfiguration(false);
        toolbar.setDisplayMode(NSToolbarDisplayMode::IconOnly);
        toolbar.setDelegate(Some(ProtocolObject::from_ref(&*toolbar)));
        toolbar
    }
    fn identifiers(&self) -> Retained<NSArray<NSString>> {
        let header = &self.ivars().node.header;
        let mut ids = Vec::new();
        if header.back_visible && header.can_pop {
            ids.push(NSString::from_str("back"));
        }
        for index in 0..header.leading {
            ids.push(NSString::from_str(&format!("action-{index}")));
        }
        ids.push(unsafe { NSToolbarFlexibleSpaceItemIdentifier }.retain());
        for index in header.leading..header.actions.len() {
            ids.push(NSString::from_str(&format!("action-{index}")));
        }
        NSArray::from_retained_slice(&ids)
    }
}
