use super::*;

pub fn run_virtual_list_smoke() {
    let mtm = MainThreadMarker::new().unwrap();
    let host = FlippedView::make(mtm);
    host.setFrame(CGRect::new(CGPoint::ZERO, CGSize::new(480., 640.)));
    let mut engine = Engine::new(Box::new(NativeMeasurer {
        radio: None,
        field: None,
        toggle: None,
        slider: None,
        label: NSTextField::new(mtm),
        button: NSButton::new(mtm),
    }));
    let mut registry = Registry {
        toolbar: None,
        navigation: HashMap::new(),
        host,
        entries: HashMap::new(),
        applied: im::HashMap::new(),
        frames: HashMap::new(),
        root: 0,
        mtm,
    };
    let mut checked_measurements = false;
    let mut recycled_nodes = 0;
    crate::platform::virtual_list_smoke::run(|bytes, rows, offset| {
        engine.flush(bytes).unwrap();
        if !checked_measurements {
            crate::platform::virtual_list_smoke::check_measurement_reuse(
                || {
                    Box::new(NativeMeasurer {
                        radio: None,
                        field: None,
                        toggle: None,
                        slider: None,
                        label: label(mtm),
                        button: NSButton::new(mtm),
                    })
                },
                engine.node(1).unwrap().clone(),
            );
            checked_measurements = true;
        }
        engine.report_layouts().unwrap();
        let transfers: Vec<_> = crate::platform::row_recycling::plan(&registry.applied, &engine)
            .into_iter()
            .map(|(old, new)| (new, Retained::as_ptr(&registry.entries[&old].view) as usize))
            .collect();
        registry.sync(&engine).unwrap();
        for (new, pointer) in transfers {
            assert_eq!(
                Retained::as_ptr(&registry.entries[&new].view) as usize,
                pointer,
                "row subtree must retain native object identity"
            );
            recycled_nodes += 1;
        }
        let scroll = registry.entries[&1]
            .extra
            .as_ref()
            .unwrap()
            .downcast_ref::<NSScrollView>()
            .unwrap();
        assert!(
            (scroll.contentView().bounds().origin.y - offset as f64).abs() < 1.,
            "AppKit offset"
        );
        if !rows.is_empty() {
            let document = scroll.documentView().unwrap();
            document.scrollPoint(CGPoint::new(0., offset as f64 + 1.));
            scroll.reflectScrolledClipView(&scroll.contentView());
            document.scrollPoint(CGPoint::new(0., offset as f64));
            scroll.reflectScrolledClipView(&scroll.contentView());
        }
        crate::platform::virtual_list_smoke::expect_scroll(
            engine.node(1).unwrap().scroll_position.0,
            offset,
        );
        for id in rows {
            unsafe {
                let _: () = msg_send![registry.entries[&(id + 1)].button.as_ref().unwrap(), performClick: std::ptr::null::<objc2::runtime::AnyObject>()];
            }
            assert_eq!(
                crate::events::dotnative_poll_click(),
                id + 1,
                "recycled callback must use current logical item"
            );
        }
        rows.iter()
            .map(|id| {
                let cell = registry.entries[&2].cells.get(*id);
                let row = &registry.entries[id].view;
                assert_eq!(unsafe { row.superview() }.as_deref(), Some(&**cell));
                assert_eq!(row.frame().origin, CGPoint::ZERO);
                assert_eq!(
                    cell.frame().size.height,
                    engine.frame(*id).unwrap().height as f64
                );
                assert_eq!(cell.subviews().len(), 1);
                Retained::as_ptr(cell) as usize
            })
            .collect()
    });
    assert_eq!(
        registry.entries[&2].view.subviews().len(),
        2,
        "empty list retains only spacers"
    );
    assert!(recycled_nodes >= 20, "complete row trees must be recycled");
    println!("PASS full row subtree recycling and native object identity");
    println!("PASS AppKit virtual cells: reuse, keyed reorder, grow/shrink, jump, empty cleanup");
}
