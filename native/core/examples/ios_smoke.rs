//! Run as a native main-thread executable inside an iOS Simulator (see README).
#[cfg(not(target_os = "ios"))]
fn main() {
    eprintln!("This smoke test requires an iOS Simulator target.");
}
#[cfg(target_os = "ios")]
fn main() {
    ios::run();
}

#[cfg(target_os = "ios")]
mod ios {
    use core::{
        platform::ios::{NativeMeasurer, NativeRegistry},
        Engine,
    };
    use objc2::{
        rc::{autoreleasepool, Weak},
        MainThreadMarker, MainThreadOnly,
    };
    use objc2_core_foundation::CGRect;
    use objc2_ui_kit::{UIButton, UIControlState, UILabel, UIScrollView, UIView};

    #[derive(Default)]
    struct Batch {
        data: Vec<u8>,
        count: u32,
    }
    impl Batch {
        fn op(&mut self, code: u8, payload: &[u8]) -> &mut Self {
            self.data.push(code);
            self.data.extend((payload.len() as u32).to_le_bytes());
            self.data.extend(payload);
            self.count += 1;
            self
        }
        fn create(&mut self, id: u32, kind: u8) -> &mut Self {
            let mut p = id.to_le_bytes().to_vec();
            p.push(kind);
            self.op(1, &p)
        }
        fn text(&mut self, id: u32, text: &str) -> &mut Self {
            let mut p = id.to_le_bytes().to_vec();
            p.extend((text.len() as u32).to_le_bytes());
            p.extend(text.as_bytes());
            self.op(2, &p)
        }
        fn style(&mut self, id: u32, padding: f32) -> &mut Self {
            let mut p = id.to_le_bytes().to_vec();
            p.extend(padding.to_le_bytes());
            p.extend([255, 0, 0, 128]);
            p.extend(8f32.to_le_bytes());
            p.extend([0, 0, 0, 255]);
            p.extend(17f32.to_le_bytes());
            self.op(3, &p)
        }
        fn children(&mut self, ids: &[u32]) -> &mut Self {
            let mut p = 1u32.to_le_bytes().to_vec();
            p.extend((ids.len() as u32).to_le_bytes());
            for id in ids {
                p.extend(id.to_le_bytes());
            }
            self.op(4, &p)
        }
        fn root(&mut self, id: u32) -> &mut Self {
            self.op(5, &id.to_le_bytes())
        }
        fn delete(&mut self, id: u32) -> &mut Self {
            self.op(6, &id.to_le_bytes())
        }
        fn viewport(&mut self, width: f32) -> &mut Self {
            let mut p = width.to_le_bytes().to_vec();
            p.extend(800f32.to_le_bytes());
            self.op(7, &p)
        }
        fn bytes(&self) -> Vec<u8> {
            let mut b = b"DNTV\x01\x00\x10\x00".to_vec();
            b.extend(((16 + self.data.len()) as u32).to_le_bytes());
            b.extend(self.count.to_le_bytes());
            b.extend(&self.data);
            b
        }
    }
    pub fn run() {
        let mtm = MainThreadMarker::new().expect("main thread");
        let weak = autoreleasepool(|_| {
            let host = UIView::initWithFrame(UIView::alloc(mtm), CGRect::ZERO);
            let mut registry = NativeRegistry::new(host.clone(), mtm);
            let mut engine = Engine::new(Box::new(NativeMeasurer::new(mtm)));
            let text = "Native multiline text wraps using UIKit metrics. Živeli! 🙂";
            let b = Batch::default()
                .viewport(320.)
                .create(1, 1)
                .style(1, 10.)
                .create(2, 2)
                .style(2, 6.)
                .text(2, text)
                .create(3, 3)
                .style(3, 8.)
                .text(3, "Tap 🙂")
                .children(&[2, 3])
                .root(1)
                .bytes();
            engine.flush(&b).unwrap();
            registry.sync(&engine).unwrap();
            assert_eq!(registry.len(), 3);
            let label = registry.view(2).unwrap().downcast_ref::<UILabel>().unwrap();
            assert_eq!(label.text().unwrap().to_string(), text);
            assert_eq!(label.layer().cornerRadius(), 8.);
            assert!(label.layer().masksToBounds());
            assert!(label.backgroundColor().is_some());
            assert_eq!(label.frame().size.width, 300.);
            let tall = label.frame().size.height;
            assert!(tall > 12.);
            let label_rect = label.textRectForBounds_limitedToNumberOfLines(label.bounds(), 0);
            assert!(label_rect.size.height > 12.);
            let button = registry
                .view(3)
                .unwrap()
                .downcast_ref::<UIButton>()
                .unwrap();
            assert_eq!(
                button
                    .titleForState(UIControlState::Normal)
                    .unwrap()
                    .to_string(),
                "Tap 🙂"
            );
            // Native press feedback is independent of managed state/rendering.
            let normal = button.backgroundColor().unwrap();
            button.setHighlighted(true);
            let pressed = button.backgroundColor().unwrap();
            let (mut nr, mut ng, mut nb, mut na) = (0., 0., 0., 0.);
            let (mut pr, mut pg, mut pb, mut pa) = (0., 0., 0., 0.);
            unsafe {
                assert!(normal.getRed_green_blue_alpha(&mut nr, &mut ng, &mut nb, &mut na));
                assert!(pressed.getRed_green_blue_alpha(&mut pr, &mut pg, &mut pb, &mut pa));
            }
            assert!(pr < nr);
            assert_eq!(pa, na);
            button.setHighlighted(false);
            assert_eq!(button.backgroundColor().unwrap(), normal);
            assert_eq!(button.alpha(), 1.0);
            // A transparent button needs foreground feedback, not invisible RGB changes.
            let mut transparent = 3u32.to_le_bytes().to_vec();
            transparent.extend(0f32.to_le_bytes());
            transparent.extend([0u8; 4]);
            transparent.extend(0f32.to_le_bytes());
            transparent.extend([0, 0, 0, 255]);
            transparent.extend(17f32.to_le_bytes());
            engine
                .flush(&Batch::default().op(3, &transparent).bytes())
                .unwrap();
            registry.sync(&engine).unwrap();
            let plain = registry
                .view(3)
                .unwrap()
                .downcast_ref::<UIButton>()
                .unwrap();
            plain.setHighlighted(true);
            assert!(plain.alpha() < 1.0);
            plain.setHighlighted(false);
            assert_eq!(plain.alpha(), 1.0);
            engine
                .flush(&Batch::default().create(4, 4).children(&[2, 3, 4]).bytes())
                .unwrap();
            registry.sync(&engine).unwrap();
            assert!(!registry
                .view(4)
                .unwrap()
                .downcast_ref::<UIScrollView>()
                .unwrap()
                .delaysContentTouches());
            engine
                .flush(&Batch::default().children(&[2, 3]).delete(4).bytes())
                .unwrap();
            registry.sync(&engine).unwrap();
            let label_identity = registry.view(2).unwrap() as *const UIView;
            let weak = Weak::new(registry.view(2).unwrap());
            engine
                .flush(&Batch::default().viewport(130.).children(&[3, 2]).bytes())
                .unwrap();
            registry.sync(&engine).unwrap();
            assert_eq!(registry.view(2).unwrap() as *const UIView, label_identity);
            assert!(engine.frame(2).unwrap().height > tall as f32);
            assert!(engine.frame(2).unwrap().y > engine.frame(3).unwrap().y);
            // Invalid candidate text must not leak to visible UIKit controls.
            assert!(engine
                .flush(&Batch::default().text(2, "wrong").children(&[999]).bytes())
                .is_err());
            assert_eq!(
                registry
                    .view(2)
                    .unwrap()
                    .downcast_ref::<UILabel>()
                    .unwrap()
                    .text()
                    .unwrap()
                    .to_string(),
                text
            );
            engine
                .flush(&Batch::default().text(2, "é\0🙂").bytes())
                .unwrap();
            registry.sync(&engine).unwrap();
            assert_eq!(
                registry
                    .view(2)
                    .unwrap()
                    .downcast_ref::<UILabel>()
                    .unwrap()
                    .text()
                    .unwrap()
                    .to_string(),
                "é\0🙂"
            );
            engine
                .flush(
                    &Batch::default()
                        .root(0)
                        .delete(2)
                        .delete(3)
                        .delete(1)
                        .bytes(),
                )
                .unwrap();
            registry.sync(&engine).unwrap();
            assert!(registry.is_empty());
            assert_eq!(host.subviews().count(), 0);
            weak
        });
        assert!(
            weak.load().is_none(),
            "Deleted view leaked after autorelease pool drained"
        );
        autoreleasepool(|_| {
            let host = UIView::initWithFrame(UIView::alloc(mtm), CGRect::ZERO);
            core::platform::ios::initialize(host.clone(), mtm).unwrap();
            let b = Batch::default()
                .viewport(200.)
                .create(1, 1)
                .create(2, 2)
                .text(2, "FFI to UIKit")
                .children(&[2])
                .root(1)
                .bytes();
            unsafe {
                core::flush_commands(b.as_ptr(), b.len());
            }
            assert_eq!(host.subviews().count(), 1);
            let b = Batch::default().root(0).delete(2).delete(1).bytes();
            unsafe {
                core::flush_commands(b.as_ptr(), b.len());
            }
            assert_eq!(host.subviews().count(), 0);
        });
        println!("PASS UIKit: native measurement, padding, wrapping, styles, keyed reorder, rollback isolation, Unicode/NUL, ARC release, FFI presentation");
    }
}
