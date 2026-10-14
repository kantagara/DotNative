use super::*;
use objc2::runtime::ProtocolObject;
use objc2_foundation::{NSArray, NSObjectProtocol};
use objc2_ui_kit::{UITabBar, UITabBarDelegate, UITabBarItem};
use std::cell::RefCell;

pub(super) struct Ivars {
    id: u32,
    items: RefCell<Vec<(u32, std::sync::Arc<str>)>>,
}
define_class!(
    #[unsafe(super(UITabBar))]
    #[thread_kind=MainThreadOnly]
    #[name = "DotNativeTabBar"]
    #[ivars=Ivars]
    pub(super) struct NativeTabBar;
    unsafe impl NSObjectProtocol for NativeTabBar {}
    unsafe impl UITabBarDelegate for NativeTabBar {
        #[unsafe(method(tabBar:didSelectItem:))]
        fn selected(&self, _bar: &UITabBar, item: &UITabBarItem) {
            crate::events::emit(self.ivars().id, 3, item.tag() as f32, String::new());
        }
    }
);
impl NativeTabBar {
    pub(super) fn new(id: u32, mtm: MainThreadMarker) -> Retained<Self> {
        let allocated = Self::alloc(mtm).set_ivars(Ivars {
            id,
            items: RefCell::default(),
        });
        let view: Retained<Self> =
            unsafe { msg_send![super(allocated), initWithFrame: CGRect::ZERO] };
        view.setDelegate(Some(ProtocolObject::from_ref(&*view)));
        view
    }
    pub(super) fn configure(&self, node: &Node) {
        if *self.ivars().items.borrow() != node.bottom_navigation.items {
            let symbols = ["house", "magnifyingglass", "star", "person", "gearshape"];
            let items: Vec<_> = node
                .bottom_navigation
                .items
                .iter()
                .enumerate()
                .map(|(i, (icon, label))| {
                    let image =
                        UIImage::systemImageNamed(&NSString::from_str(symbols[*icon as usize]));
                    UITabBarItem::initWithTitle_image_tag(
                        UITabBarItem::alloc(self.mtm()),
                        Some(&NSString::from_str(label)),
                        image.as_deref(),
                        i as NSInteger,
                    )
                })
                .collect();
            self.setItems(Some(&NSArray::from_retained_slice(&items)));
            *self.ivars().items.borrow_mut() = node.bottom_navigation.items.clone();
        }
        if let Some(items) = self.items() {
            if node.bottom_navigation.selected < items.len() {
                self.setSelectedItem(Some(&items.objectAtIndex(node.bottom_navigation.selected)));
            }
        }
    }
}
