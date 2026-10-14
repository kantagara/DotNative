use super::*;

pub(super) fn run() {
    let mut engine = Engine::new(Box::new(NativeMeasurer::default()));
    let mut registry = Registry {
        navigation: HashMap::new(),
        host: gtk::Fixed::new(),
        entries: HashMap::new(),
    };
    // Real GTK allocations catch requested-vs-natural-size mistakes in Fixed.
    let window = gtk::Window::new();
    window.set_default_size(480, 640);
    window.set_child(Some(&registry.host));
    window.present();
    let mut checked_measurements = false;
    let mut recycled_nodes = 0;
    crate::platform::virtual_list_smoke::run(|bytes, rows, offset| {
        engine.flush(bytes).unwrap();
        if !checked_measurements {
            crate::platform::virtual_list_smoke::check_measurement_reuse(
                || Box::new(NativeMeasurer::default()),
                engine.node(1).unwrap().clone(),
            );
            checked_measurements = true;
        }
        engine.report_layouts().unwrap();
        let transfers: Vec<_> = crate::platform::row_recycling::plan(
            &registry
                .entries
                .iter()
                .filter_map(|(id, e)| e.applied.as_ref().map(|n| (*id, n.clone())))
                .collect(),
            &engine,
        )
        .into_iter()
        .map(|(old, new)| (new, registry.entries[&old].surface.as_ptr() as usize))
        .collect();
        registry.sync(&engine).unwrap();
        for (new, pointer) in transfers {
            assert_eq!(
                registry.entries[&new].surface.as_ptr() as usize,
                pointer,
                "row subtree must retain native object identity"
            );
            recycled_nodes += 1;
        }
        let deadline = std::time::Instant::now() + std::time::Duration::from_millis(60);
        while std::time::Instant::now() < deadline {
            while glib::MainContext::default().pending() {
                glib::MainContext::default().iteration(false);
            }
            std::thread::sleep(std::time::Duration::from_millis(1));
        }
        let adjustment = registry.entries[&1].scroll.as_ref().unwrap().vadjustment();
        assert!(
            (adjustment.value() - offset as f64).abs() < 1.,
            "GTK offset: {} vs {offset}",
            adjustment.value()
        );
        assert!(
            (adjustment.page_size() - 320.).abs() < 1.,
            "GTK viewport: {}",
            adjustment.page_size()
        );
        if !rows.is_empty() {
            adjustment.set_value(offset as f64 + 1.);
            adjustment.set_value(offset as f64);
        }
        crate::platform::virtual_list_smoke::expect_scroll(
            engine.node(1).unwrap().scroll_position.0,
            offset,
        );
        for id in rows {
            registry.entries[&(id + 1)]
                .control
                .as_ref()
                .unwrap()
                .downcast_ref::<gtk::Button>()
                .unwrap()
                .emit_clicked();
            assert_eq!(
                crate::events::dotnative_poll_click(),
                id + 1,
                "recycled callback must use current logical item"
            );
        }
        rows.iter()
            .map(|id| {
                let cell = registry.entries[&2].cells.get(*id);
                let row = &registry.entries[id].surface;
                assert_eq!(row.parent().as_ref(), Some(cell.upcast_ref()));
                assert_eq!(cell.first_child().as_ref(), Some(row.upcast_ref()));
                assert!(row.next_sibling().is_none());
                assert_eq!(
                    cell.height(),
                    engine.frame(*id).unwrap().height.ceil() as i32
                );
                cell.as_ptr() as usize
            })
            .collect()
    });
    let document = &registry.entries[&2].content;
    assert_eq!(
        document.observe_children().n_items(),
        2,
        "empty list retains only spacers"
    );
    window.close();
    assert!(recycled_nodes >= 20, "complete row trees must be recycled");
    println!("PASS full row subtree recycling and native object identity");
    println!("PASS GTK virtual cells: reuse, keyed reorder, grow/shrink, jump, empty cleanup");
}
