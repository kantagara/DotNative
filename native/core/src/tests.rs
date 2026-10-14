use super::*;
use crate::protocol::parse;

#[derive(Default)]
struct Batch {
    payload: Vec<u8>,
    count: u32,
}
impl Batch {
    fn op(&mut self, op: u8, p: &[u8]) -> &mut Self {
        self.payload.push(op);
        self.payload.extend((p.len() as u32).to_le_bytes());
        self.payload.extend(p);
        self.count += 1;
        self
    }
    fn create(&mut self, id: u32, kind: u8) -> &mut Self {
        let mut p = id.to_le_bytes().to_vec();
        p.push(kind);
        self.op(1, &p)
    }
    fn view_type(&mut self, id: u32, name: &str) -> &mut Self {
        let mut p = id.to_le_bytes().to_vec();
        p.extend((name.len() as u32).to_le_bytes());
        p.extend(name.as_bytes());
        self.op(41, &p)
    }
    fn text(&mut self, id: u32, text: &str) -> &mut Self {
        let mut p = id.to_le_bytes().to_vec();
        p.extend((text.len() as u32).to_le_bytes());
        p.extend(text.as_bytes());
        self.op(2, &p)
    }
    fn children(&mut self, id: u32, children: &[u32]) -> &mut Self {
        let mut p = id.to_le_bytes().to_vec();
        p.extend((children.len() as u32).to_le_bytes());
        for child in children {
            p.extend(child.to_le_bytes());
        }
        self.op(4, &p)
    }
    fn root(&mut self, id: u32) -> &mut Self {
        self.op(5, &id.to_le_bytes())
    }
    fn delete(&mut self, id: u32) -> &mut Self {
        self.op(6, &id.to_le_bytes())
    }
    fn viewport(&mut self, w: f32, h: f32) -> &mut Self {
        let mut p = w.to_le_bytes().to_vec();
        p.extend(h.to_le_bytes());
        self.op(7, &p)
    }
    fn padding(&mut self, id: u32, padding: f32) -> &mut Self {
        let mut p = id.to_le_bytes().to_vec();
        p.extend(padding.to_le_bytes());
        p.extend([0; 4]);
        p.extend(0f32.to_le_bytes());
        p.extend([0, 0, 0, 255]);
        p.extend(17f32.to_le_bytes());
        self.op(3, &p)
    }
    fn bytes(&self) -> Vec<u8> {
        let mut b = b"DNTV\x01\x00\x10\x00".to_vec();
        b.extend(((16 + self.payload.len()) as u32).to_le_bytes());
        b.extend(self.count.to_le_bytes());
        b.extend(&self.payload);
        b
    }
}
// Explicit fixture metrics, only compiled in tests. Production uses OS measurement.
struct FixtureMeasure;
impl Measure for FixtureMeasure {
    fn navigation_bar_height(&mut self, _: f32) -> f32 {
        54.
    }
    fn measure(
        &mut self,
        node: &Node,
        known: Size<Option<f32>>,
        _: Size<AvailableSpace>,
    ) -> Result<Size<f32>> {
        Ok(Size {
            width: known.width.unwrap_or(40.),
            height: known.height.unwrap_or(if node.text.as_ref() == "tall" {
                40.
            } else {
                20.
            }),
        })
    }
}
pub(crate) fn engine() -> Engine {
    Engine::new(Box::new(FixtureMeasure))
}
pub(crate) fn initial() -> Vec<u8> {
    Batch::default()
        .viewport(200., 300.)
        .create(1, 1)
        .padding(1, 10.)
        .create(2, 2)
        .text(2, "é\0🙂")
        .create(3, 3)
        .text(3, "button")
        .children(1, &[2, 3])
        .root(1)
        .bytes()
}
#[test]
fn layout_reorder_text_invalidation_and_resize() {
    let mut e = engine();
    e.flush(&initial()).unwrap();
    assert_eq!(
        e.frame(1).unwrap(),
        Frame {
            x: 0.,
            y: 0.,
            width: 200.,
            height: 300.
        }
    );
    assert_eq!(
        e.frame(2).unwrap(),
        Frame {
            x: 10.,
            y: 10.,
            width: 180.,
            height: 20.
        }
    );
    assert_eq!(e.frame(3).unwrap().y, 30.);
    assert_eq!(&*e.node(2).unwrap().text, "é\0🙂");
    e.flush(
        &Batch::default()
            .children(1, &[3, 2])
            .text(3, "tall")
            .viewport(250., 300.)
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.frame(2).unwrap().y, 50.);
    assert_eq!(e.frame(2).unwrap().width, 230.);
    e.flush(&Batch::default().padding(1, 0.).bytes()).unwrap();
    assert_eq!(e.frame(3).unwrap().y, 0.);
}
#[test]
fn padding_on_leaves_is_applied_once() {
    let mut e = engine();
    e.flush(&initial()).unwrap();
    e.flush(&Batch::default().padding(2, 5.).bytes()).unwrap();
    assert_eq!(e.frame(2).unwrap().height, 30.);
    assert_eq!(e.frame(3).unwrap().y, 40.);
}
#[test]
fn malformed_batches_leave_committed_state_untouched() {
    let mut e = engine();
    e.flush(&initial()).unwrap();
    for b in [
        Batch::default().children(1, &[2, 2]).bytes(),
        Batch::default().children(1, &[1]).bytes(),
        Batch::default().children(1, &[999]).bytes(),
        Batch::default().create(4, 1).bytes(),
        Batch::default().delete(2).bytes(),
        Batch::default().text(1, "no").bytes(),
        Batch::default().children(2, &[3]).bytes(),
        Batch::default().create(2, 2).bytes(),
        Batch::default().padding(1, f32::NAN).bytes(),
        Batch::default().viewport(f32::INFINITY, 0.).bytes(),
        Batch::default().op(99, &[0; 4]).bytes(),
    ] {
        assert!(e.flush(&b).is_err());
        assert_eq!(e.node_count(), 3);
        assert_eq!(e.frame(2).unwrap().y, 10.);
    }
}
#[test]
fn truncation_and_corrupt_lengths_never_panic() {
    let bytes = initial();
    for end in 0..bytes.len() {
        assert!(parse(&bytes[..end]).is_err());
    }
    let mut corrupt = bytes.clone();
    corrupt[12..16].copy_from_slice(&u32::MAX.to_le_bytes());
    assert!(parse(&corrupt).is_err());
    let mut corrupt = bytes.clone();
    corrupt[17..21].copy_from_slice(&u32::MAX.to_le_bytes());
    assert!(parse(&corrupt).is_err());
    let mut invalid = Batch::default().text(1, "x").bytes();
    *invalid.last_mut().unwrap() = 255;
    assert!(parse(&invalid).is_err());
    let mut rng = 123u32;
    for _ in 0..5000 {
        let mut b = bytes.clone();
        rng = rng.wrapping_mul(1664525).wrapping_add(1013904223);
        let index = rng as usize % b.len();
        b[index] ^= (rng >> 24) as u8;
        let mut e = engine();
        let _ = e.flush(&b);
    }
}
#[test]
fn replace_subtree_and_delete_postorder() {
    let mut e = engine();
    e.flush(&initial()).unwrap();
    e.flush(
        &Batch::default()
            .create(4, 1)
            .create(5, 1)
            .children(4, &[5])
            .root(4)
            .delete(2)
            .delete(3)
            .delete(1)
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.node_count(), 2);
    e.flush(&Batch::default().root(0).delete(5).delete(4).bytes())
        .unwrap();
    assert_eq!(e.node_count(), 0);
    assert!(e
        .flush(&Batch::default().create(1, 1).root(1).bytes())
        .is_err());
}
#[test]
fn missing_native_measurement_is_explicit_and_atomic() {
    let mut e = Engine::new(Box::new(RequireNativeMeasure));
    assert!(matches!(e.flush(&initial()), Err(Error::Measurement(_))));
    assert_eq!(e.node_count(), 0);
}
#[test]
fn input_is_owned_after_flush() {
    let mut e = engine();
    let mut bytes = initial();
    e.flush(&bytes).unwrap();
    bytes.fill(0);
    assert_eq!(&*e.node(2).unwrap().text, "é\0🙂");
}
#[test]
// Native platforms require an OS main-loop host; their FFI path is covered by
// the native runner smoke tests rather than this headless fixture.
#[cfg(not(any(
    target_os = "ios",
    target_os = "macos",
    target_os = "android",
    all(feature = "desktop", any(target_os = "linux", target_os = "windows"))
)))]
fn ffi_valid_container_batch() {
    let b = Batch::default()
        .viewport(200., 300.)
        .create(1, 1)
        .padding(1, 10.)
        .root(1)
        .bytes();
    unsafe {
        flush_commands(b.as_ptr(), b.len());
    }
    ENGINE.with(|slot| {
        assert_eq!(
            slot.borrow().as_ref().unwrap().frame(1).unwrap().width,
            200.
        )
    });
}
#[test]
fn ffi_rejects_invalid_input_in_subprocess() {
    if std::env::var_os("DOTNATIVE_ABORT_TEST").is_some() {
        unsafe {
            flush_commands(std::ptr::null(), 16);
        }
        unreachable!();
    }
    let output = std::process::Command::new(std::env::current_exe().unwrap())
        .args(["--exact", "tests::ffi_rejects_invalid_input_in_subprocess"])
        .env("DOTNATIVE_ABORT_TEST", "1")
        .output()
        .unwrap();
    assert!(!output.status.success());
}

#[test]
fn navigation_page_bounds_scroll_without_clipping_content_extent() {
    let mut e = engine();
    let mut control = 1u32.to_le_bytes().to_vec();
    for value in [1f32, 0., 1.] {
        control.extend(value.to_le_bytes());
    }
    control.extend([0u8; 12]);
    let mut batch = Batch::default();
    batch
        .viewport(320., 600.)
        .create(1, 10)
        .create(2, 11)
        .create(3, 4)
        .create(4, 1);
    let mut children = vec![];
    for id in 5..105 {
        batch.create(id, 2).text(id, "row");
        children.push(id);
    }
    batch
        .children(4, &children)
        .children(3, &[4])
        .children(2, &[3])
        .children(1, &[2])
        .op(9, &control)
        .root(1);
    e.flush(&batch.bytes()).unwrap();
    assert_eq!(e.frame(2).unwrap().height, 600.);
    assert_eq!(e.frame(3).unwrap().height, 600.);
    assert_eq!(e.frame(4).unwrap().height, 2000.);
}

#[test]
fn radio_boolean_selection_is_validated_transactionally() {
    let mut e = engine();
    let mut initial = Batch::default();
    initial
        .viewport(320., 600.)
        .create(1, 12)
        .text(1, "System")
        .root(1);
    e.flush(&initial.bytes()).unwrap();
    let control = |value: f32| {
        let mut bytes = 1u32.to_le_bytes().to_vec();
        for n in [value, 0., 1.] {
            bytes.extend(n.to_le_bytes());
        }
        bytes.extend([0u8; 12]);
        Batch::default().op(9, &bytes).bytes()
    };
    e.flush(&control(1.)).unwrap();
    assert_eq!(e.node(1).unwrap().control.value, 1.);
    assert!(e.flush(&control(0.5)).is_err());
    assert_eq!(e.node(1).unwrap().control.value, 1.);
    e.flush(&control(0.)).unwrap();
    assert_eq!(e.node(1).unwrap().control.value, 0.);
}

#[test]
fn header_payload_is_bounded_and_transactional() {
    let mut e = engine();
    let mut initial = Batch::default();
    initial.viewport(320., 600.).create(1, 13).root(1);
    e.flush(&initial.bytes()).unwrap();
    let payload = |flags: u32, count: u32, title: &[u8]| {
        let mut bytes = vec![];
        for value in [1u32, flags, count, 0, title.len() as u32] {
            bytes.extend(value.to_le_bytes());
        }
        bytes.extend(title);
        Batch::default().op(11, &bytes).bytes()
    };
    e.flush(&payload(3, 1, b"Filter")).unwrap();
    for bad in [
        payload(16, 1, b"Filter"),
        payload(3, 33, b"Filter"),
        payload(3, 1, &[255]),
        payload(3, 1, b""),
    ] {
        assert!(e.flush(&bad).is_err());
        assert_eq!(&*e.node(1).unwrap().header.actions[0], "Filter");
    }
    e.flush(&payload(2, 1, b"Edit")).unwrap();
    assert!(!e.node(1).unwrap().header.visible);
}

#[test]
fn toolbar_leaves_a_bounded_scroll_viewport() {
    let mut e = engine();
    let mut batch = Batch::default();
    batch
        .viewport(320., 600.)
        .create(1, 1)
        .create(2, 13)
        .create(3, 4)
        .create(4, 1);
    for id in 5..105 {
        batch.create(id, 2).text(id, "row");
    }
    let layout = |id: u32, height: f32, grow: f32| {
        let mut p = id.to_le_bytes().to_vec();
        for value in [-1f32, height, 0., grow] {
            p.extend(value.to_le_bytes());
        }
        p.extend([0u8; 4]);
        p
    };
    batch
        .op(8, &layout(2, 56., 0.))
        .op(8, &layout(3, 0., 1.))
        .children(4, &(5..105).collect::<Vec<_>>())
        .children(3, &[4])
        .children(1, &[2, 3])
        .root(1);
    e.flush(&batch.bytes()).unwrap();
    assert_eq!(e.frame(3).unwrap().y, 56.);
    assert_eq!(e.frame(3).unwrap().height, 544.);
    assert_eq!(e.frame(4).unwrap().height, 2000.);
    e.flush(&Batch::default().op(8, &layout(2, 0., 0.)).bytes())
        .unwrap();
    assert_eq!(e.frame(3).unwrap().height, 600.);
    assert_eq!(e.frame(4).unwrap().height, 2000.);
}

#[test]
fn nested_scroll_policy_is_validated_transactionally() {
    let mut e = engine();
    e.flush(
        &Batch::default()
            .viewport(320., 600.)
            .create(1, 4)
            .root(1)
            .bytes(),
    )
    .unwrap();
    assert!(e.node(1).unwrap().contain_scroll);
    let set = |mode| {
        let mut p = 1u32.to_le_bytes().to_vec();
        p.push(mode);
        Batch::default().op(12, &p).bytes()
    };
    e.flush(&set(0)).unwrap();
    assert!(!e.node(1).unwrap().contain_scroll);
    assert!(e.flush(&set(2)).is_err());
    assert!(!e.node(1).unwrap().contain_scroll);
    e.flush(&set(1)).unwrap();
    assert!(e.node(1).unwrap().contain_scroll);
}

#[test]
fn header_opacity_survives_other_header_updates() {
    let mut e = engine();
    e.flush(
        &Batch::default()
            .viewport(320., 600.)
            .create(1, 11)
            .root(1)
            .bytes(),
    )
    .unwrap();
    let opacity = |value: f32| {
        let mut p = 1u32.to_le_bytes().to_vec();
        p.extend(value.to_le_bytes());
        Batch::default().op(13, &p).bytes()
    };
    e.flush(&opacity(0.5)).unwrap();
    let flags: Vec<u8> = [1u32, 11, 0, 0]
        .iter()
        .flat_map(|n| n.to_le_bytes())
        .collect();
    e.flush(&Batch::default().op(11, &flags).bytes()).unwrap();
    assert_eq!(e.node(1).unwrap().header.opacity, 0.5);
    assert!(e.node(1).unwrap().header.large_title);
    for invalid in [-1., 1.1, f32::NAN] {
        assert!(e.flush(&opacity(invalid)).is_err());
    }
    assert_eq!(e.node(1).unwrap().header.opacity, 0.5);
}

fn layout_payload(id: u32, width: f32, height: f32, direction: u8) -> Vec<u8> {
    let mut p = id.to_le_bytes().to_vec();
    for value in [width, height, 0., 0.] {
        p.extend(value.to_le_bytes());
    }
    p.extend([direction, 0, 0, 0]);
    p
}
fn shrink_payload(id: u32, shrink: f32) -> Vec<u8> {
    let mut p = id.to_le_bytes().to_vec();
    p.extend(shrink.to_le_bytes());
    p
}
fn axis_payload(id: u32, axis: u8) -> Vec<u8> {
    let mut p = id.to_le_bytes().to_vec();
    p.push(axis);
    p
}

#[test]
fn shrink_is_opt_in_can_reset_and_rejects_invalid_values() {
    let mut e = engine();
    e.flush(
        &Batch::default()
            .viewport(100., 100.)
            .create(1, 1)
            .op(8, &layout_payload(1, -1., -1., 1))
            .create(2, 2)
            .text(2, "long")
            .op(8, &layout_payload(2, 200., 20., 0))
            .create(3, 1)
            .op(8, &layout_payload(3, 40., 20., 0))
            .children(1, &[2, 3])
            .root(1)
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.frame(2).unwrap().width, 200.);
    e.flush(&Batch::default().op(14, &shrink_payload(2, 1.)).bytes())
        .unwrap();
    assert_eq!(e.frame(2).unwrap().width, 60.);
    assert_eq!(e.frame(3).unwrap().x, 60.);
    // A legacy SetLayout command must preserve the separately set shrink factor.
    e.flush(
        &Batch::default()
            .op(8, &layout_payload(2, 180., 20., 0))
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.frame(2).unwrap().width, 60.);
    for value in [-1., f32::NAN, f32::INFINITY] {
        assert!(e
            .flush(&Batch::default().op(14, &shrink_payload(2, value)).bytes())
            .is_err());
        assert_eq!(e.frame(2).unwrap().width, 60.);
    }
    e.flush(&Batch::default().op(14, &shrink_payload(2, 0.)).bytes())
        .unwrap();
    assert_eq!(e.frame(2).unwrap().width, 180.);
    // Vertical shrink follows the same policy.
    e.flush(
        &Batch::default()
            .op(8, &layout_payload(1, -1., -1., 0))
            .op(8, &layout_payload(2, 40., 200., 0))
            .op(14, &shrink_payload(2, 1.))
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.frame(2).unwrap().height, 80.);
}

#[test]
fn scroll_axis_is_independent_and_updates_without_replacing_nodes() {
    let mut e = engine();
    e.flush(
        &Batch::default()
            .viewport(100., 80.)
            .create(1, 4)
            .op(15, &axis_payload(1, 1))
            .create(2, 1)
            .op(8, &layout_payload(2, -1., -1., 1))
            .create(3, 1)
            .op(8, &layout_payload(3, 150., 50., 0))
            .create(4, 1)
            .op(8, &layout_payload(4, 150., 50., 0))
            .children(2, &[3, 4])
            .children(1, &[2])
            .root(1)
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.node(1).unwrap().layout.direction, 0);
    assert_eq!(e.node(1).unwrap().scroll_axis, 1);
    assert_eq!(e.frame(1).unwrap().width, 100.);
    assert_eq!(e.frame(4).unwrap().x, 150.);
    // Content extent must be represented by the child frame, used by native hosts.
    assert_eq!(e.frame(2).unwrap().width, 300.);
    assert!(e
        .flush(&Batch::default().op(15, &axis_payload(1, 2)).bytes())
        .is_err());
    assert!(e
        .flush(&Batch::default().op(15, &axis_payload(2, 1)).bytes())
        .is_err());
    assert_eq!(e.node(1).unwrap().scroll_axis, 1);
    e.flush(
        &Batch::default()
            .op(15, &axis_payload(1, 0))
            .op(8, &layout_payload(2, -1., -1., 0))
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.frame(2).unwrap().height, 100.);
    assert_eq!(e.frame(4).unwrap().y, 50.);
}

#[test]
fn staging_cache_survives_measurement_failure_and_structural_changes() {
    struct FailingMeasure;
    impl Measure for FailingMeasure {
        fn measure(
            &mut self,
            node: &Node,
            known: Size<Option<f32>>,
            available: Size<AvailableSpace>,
        ) -> Result<Size<f32>> {
            if node.text.as_ref() == "fail" {
                return Err(Error::Measurement("injected failure"));
            }
            FixtureMeasure.measure(node, known, available)
        }
    }
    let mut e = Engine::new(Box::new(FailingMeasure));
    e.flush(&initial()).unwrap();
    // Repeatedly revisit both cached generations, including reverting text.
    for text in ["tall", "short", "tall", "short", "tall"] {
        e.flush(&Batch::default().text(2, text).bytes()).unwrap();
        assert_eq!(
            e.frame(2).unwrap().height,
            if text == "tall" { 40. } else { 20. }
        );
    }
    let before = e.frame(2).unwrap();
    assert!(e
        .flush(
            &Batch::default()
                .create(4, 2)
                .text(4, "fail")
                .children(1, &[4, 3])
                .delete(2)
                .bytes()
        )
        .is_err());
    assert_eq!(e.frame(2).unwrap(), before);
    assert!(e.node(4).is_none());
    assert_eq!(e.node(1).unwrap().children, vec![2, 3]);
    // Rejected IDs and deletes must not leak into the next transaction.
    e.flush(
        &Batch::default()
            .create(4, 2)
            .text(4, "tall")
            .children(1, &[4, 3])
            .delete(2)
            .bytes(),
    )
    .unwrap();
    for _ in 0..4 {
        e.flush(
            &Batch::default()
                .children(1, &[3, 4])
                .text(4, "short")
                .bytes(),
        )
        .unwrap();
        assert_eq!(e.frame(4).unwrap().y, 30.);
        assert_eq!(e.frame(4).unwrap().height, 20.);
        e.flush(
            &Batch::default()
                .children(1, &[4, 3])
                .text(4, "tall")
                .bytes(),
        )
        .unwrap();
        assert_eq!(e.frame(3).unwrap().y, 50.);
    }
    e.flush(
        &Batch::default()
            .root(0)
            .delete(4)
            .delete(3)
            .delete(1)
            .bytes(),
    )
    .unwrap();
    e.flush(&Batch::default().create(5, 1).root(5).bytes())
        .unwrap();
    e.flush(&Batch::default().viewport(123., 234.).bytes())
        .unwrap();
    assert_eq!(e.frame(5).unwrap().width, 123.);
}

#[test]
fn navigation_pages_overlap_at_the_navigation_origin() {
    let mut control = 1u32.to_le_bytes().to_vec();
    for value in [2f32, 0., 2.] {
        control.extend(value.to_le_bytes());
    }
    control.extend([0u8; 12]);
    let mut e = engine();
    e.flush(
        &Batch::default()
            .viewport(320., 600.)
            .create(1, 10)
            .create(2, 11)
            .create(3, 11)
            .children(1, &[2, 3])
            .op(9, &control)
            .root(1)
            .bytes(),
    )
    .unwrap();
    for _ in 0..3 {
        assert_eq!(e.frame(2).unwrap(), e.frame(3).unwrap());
        assert_eq!(e.frame(2).unwrap().x, 0.);
        assert_eq!(e.frame(2).unwrap().y, 0.);
        e.flush(&Batch::default().viewport(600., 320.).bytes())
            .unwrap();
    }
}

#[test]
fn desktop_shell_keeps_navigation_pages_inside_its_content_viewport() {
    let mut control = 2u32.to_le_bytes().to_vec();
    for value in [2f32, 0., 2.] {
        control.extend(value.to_le_bytes());
    }
    control.extend([0u8; 12]);
    let mut sidebar = 1u32.to_le_bytes().to_vec();
    sidebar.extend((-1i32).to_le_bytes());
    sidebar.extend(1u32.to_le_bytes());
    sidebar.extend(260f32.to_le_bytes());
    sidebar.extend([0u8; 12]); // no items, system theme, empty title
    let mut e = engine();
    e.flush(
        &Batch::default()
            .viewport(800., 600.)
            .create(1, 15)
            .create(2, 10)
            .create(3, 11)
            .create(4, 11)
            .children(1, &[2])
            .children(2, &[3, 4])
            .padding(1, 20.)
            .op(9, &control)
            .op(33, &sidebar)
            .root(1)
            .bytes(),
    )
    .unwrap();
    for (padding, width, height) in [(20., 760., 560.), (40., 720., 520.), (0., 800., 600.)] {
        e.flush(&Batch::default().padding(1, padding).bytes())
            .unwrap();
        let host = e.frame(2).unwrap();
        assert_eq!(
            (host.x, host.y, host.width, host.height),
            (padding, padding, width, height)
        );
        for id in [3, 4] {
            let page = e.frame(id).unwrap();
            assert_eq!(
                (page.x, page.y, page.width, page.height),
                (0., 0., width, height - 54.)
            );
        }
    }
}

#[test]
fn navigation_transition_is_validated_transactionally_without_layout() {
    let mut control = 1u32.to_le_bytes().to_vec();
    for value in [1f32, 0., 1.] {
        control.extend(value.to_le_bytes());
    }
    control.extend([0u8; 12]);
    let mut e = engine();
    e.flush(
        &Batch::default()
            .viewport(320., 600.)
            .create(1, 10)
            .create(2, 11)
            .children(1, &[2])
            .op(9, &control)
            .root(1)
            .bytes(),
    )
    .unwrap();
    let passes = e.layout_passes;
    let transition = |id: u32, kind: u8, duration: f32, easing: u8| {
        let mut bytes = id.to_le_bytes().to_vec();
        bytes.push(kind);
        bytes.extend(duration.to_le_bytes());
        bytes.push(easing);
        Batch::default().op(31, &bytes).bytes()
    };
    e.flush(&transition(1, 2, 0.24, 2)).unwrap();
    assert_eq!(e.node(1).unwrap().navigation_transition, 2);
    assert_eq!(e.node(1).unwrap().navigation_duration, 0.24);
    assert_eq!(e.node(1).unwrap().navigation_easing, 2);
    assert_eq!(e.layout_passes, passes);
    for invalid in [
        transition(1, 4, 0.24, 2),
        transition(1, 2, 0., 2),
        transition(1, 2, 0.24, 4),
        transition(2, 2, 0.24, 2),
    ] {
        assert!(e.flush(&invalid).is_err());
        assert_eq!(e.node(1).unwrap().navigation_duration, 0.24);
    }
}

#[test]
fn unchanged_nodes_share_storage_across_flushes() {
    let mut e = engine();
    e.flush(&initial()).unwrap();
    let unchanged = e.node(3).unwrap() as *const Node;
    for text in ["one", "two", "three", "four"] {
        e.flush(&Batch::default().text(2, text).bytes()).unwrap();
        assert_eq!(unchanged, e.node(3).unwrap() as *const Node);
    }
}

#[test]
#[ignore = "manual release-mode timing, not a CI performance threshold"]
fn large_tree_text_update_timing() {
    for count in [1_000, 10_000] {
        let mut e = engine();
        let mut batch = Batch::default();
        batch.viewport(320., 600.).create(1, 1);
        for id in 2..=count {
            batch.create(id, 2).text(id, "row");
        }
        batch.children(1, &(2..=count).collect::<Vec<_>>()).root(1);
        e.flush(&batch.bytes()).unwrap();
        let updates: Vec<_> = (0..100)
            .map(|i| {
                Batch::default()
                    .text(2, if i % 2 == 0 { "tall" } else { "row" })
                    .bytes()
            })
            .collect();
        for bytes in &updates[..4] {
            e.flush(bytes).unwrap();
        }
        let start = std::time::Instant::now();
        for bytes in &updates {
            e.flush(bytes).unwrap();
        }
        eprintln!("{count} nodes: {:?} / 100 text updates", start.elapsed());
    }
}

#[test]
fn presentation_commits_skip_layout_and_preserve_staging_generations() {
    let control = |id: u32, value: f32, placeholder: &str| {
        let mut payload = id.to_le_bytes().to_vec();
        for value in [value, 0., 1.] {
            payload.extend(value.to_le_bytes());
        }
        payload.extend(0u32.to_le_bytes());
        payload.extend(0u32.to_le_bytes());
        payload.extend((placeholder.len() as u32).to_le_bytes());
        payload.extend(placeholder.as_bytes());
        payload
    };
    let mut e = engine();
    e.flush(
        &Batch::default()
            .viewport(320., 600.)
            .create(1, 1)
            .create(2, 6)
            .create(3, 7)
            .create(4, 5)
            .children(1, &[2, 3, 4])
            .root(1)
            .bytes(),
    )
    .unwrap();
    let before = e.frame(4).unwrap();
    let passes = e.layout_passes;
    for value in [0.25, 0.5, 0.75] {
        e.flush(
            &Batch::default()
                .op(9, &control(2, 1., ""))
                .op(9, &control(3, value, ""))
                .bytes(),
        )
        .unwrap();
    }
    assert_eq!(e.layout_passes, passes);
    assert_eq!(e.frame(4).unwrap(), before);
    assert_eq!(e.node(3).unwrap().control.value, 0.75);

    // The spare still describes its original generation. Subsequent changes
    // must catch up, including nodes introduced before the skipped batches.
    e.flush(&Batch::default().op(9, &control(4, 0., "hint")).bytes())
        .unwrap();
    assert_eq!(e.layout_passes, passes + 1);
    e.flush(&Batch::default().text(4, "tall").bytes()).unwrap();
    assert_eq!(e.frame(4).unwrap().height, 40.);
    assert_eq!(e.node(3).unwrap().control.value, 0.75);
    let passes = e.layout_passes;
    assert!(e
        .flush(&Batch::default().op(9, &control(1, 0., "")).bytes())
        .is_err());
    assert_eq!(e.layout_passes, passes);
    assert_eq!(e.frame(4).unwrap().height, 40.);
}

#[test]
fn header_opacity_only_relayouts_when_page_inset_policy_changes() {
    let mut control = 1u32.to_le_bytes().to_vec();
    for value in [1f32, 0., 1.] {
        control.extend(value.to_le_bytes());
    }
    control.extend([0u8; 12]);
    let mut e = engine();
    e.flush(
        &Batch::default()
            .viewport(320., 600.)
            .create(1, 10)
            .create(2, 11)
            .create(3, 1)
            .children(2, &[3])
            .children(1, &[2])
            .op(9, &control)
            .root(1)
            .bytes(),
    )
    .unwrap();
    let set_opacity = |value: f32| {
        let mut payload = 2u32.to_le_bytes().to_vec();
        payload.extend(value.to_le_bytes());
        Batch::default().op(13, &payload).bytes()
    };
    assert_eq!(e.frame(3).unwrap().height, 546.);
    e.flush(&set_opacity(0.5)).unwrap();
    assert_eq!(e.frame(3).unwrap().height, 600.);
    let passes = e.layout_passes;
    e.flush(&set_opacity(0.25)).unwrap();
    assert_eq!(e.layout_passes, passes);
    e.flush(&set_opacity(1.)).unwrap();
    assert_eq!(e.layout_passes, passes + 1);
    assert_eq!(e.frame(3).unwrap().height, 546.);
}

fn extended_layout_payload(
    id: u32,
    margin: [f32; 4],
    limits: [f32; 5],
    wrap: u8,
    align: u8,
    padding: [f32; 4],
) -> Vec<u8> {
    let mut p = id.to_le_bytes().to_vec();
    for value in margin.into_iter().chain(limits) {
        p.extend(value.to_le_bytes());
    }
    p.extend([wrap, align]);
    for value in padding {
        p.extend(value.to_le_bytes());
    }
    p
}

#[test]
fn extended_layout_applies_constraints_margins_padding_and_resets() {
    let mut e = engine();
    let mut b = Batch::default();
    b.create(1, 1)
        .create(2, 2)
        .text(2, "content")
        .children(1, &[2])
        .root(1)
        .viewport(200., 200.);
    b.op(
        16,
        &extended_layout_payload(
            2,
            [10., 20., 30., 40.],
            [60., 50., 100., 80., -1.],
            0,
            2,
            [2., 3., 4., 5.],
        ),
    );
    e.flush(&b.bytes()).unwrap();
    let frame = e.frame(2).unwrap();
    assert_eq!(
        (frame.x, frame.y, frame.width, frame.height),
        (10., 20., 60., 50.)
    );
    assert_eq!(e.node(2).unwrap().padding(), [2., 3., 4., 5.]);
    let mut reset = Batch::default();
    reset.op(
        16,
        &extended_layout_payload(2, [0.; 4], [-1.; 5], 0, 0, [-1.; 4]),
    );
    e.flush(&reset.bytes()).unwrap();
    assert_eq!(e.frame(2).unwrap().width, 200.);
    assert_eq!(e.frame(2).unwrap().y, 0.);
    for bad in [f32::NAN, f32::INFINITY, -2.] {
        let mut invalid = Batch::default();
        invalid.op(
            16,
            &extended_layout_payload(2, [0.; 4], [bad, -1., -1., -1., -1.], 0, 0, [-1.; 4]),
        );
        assert!(e.flush(&invalid.bytes()).is_err());
        assert_eq!(e.frame(2).unwrap().width, 200.);
    }
    for (wrap, align, limits, padding) in [
        (3, 0, [-1.; 5], [-1.; 4]),
        (0, 5, [-1.; 5], [-1.; 4]),
        (0, 0, [100., -1., 50., -1., -1.], [-1.; 4]),
        (0, 0, [-1.; 5], [0., -1., -1., -1.]),
    ] {
        let mut invalid = Batch::default();
        invalid.op(
            16,
            &extended_layout_payload(2, [0.; 4], limits, wrap, align, padding),
        );
        assert!(e.flush(&invalid.bytes()).is_err());
    }
}

#[test]
fn extended_layout_wrap_and_basis_change_real_frames() {
    let mut e = engine();
    let mut b = Batch::default();
    b.create(1, 1)
        .create(2, 2)
        .create(3, 2)
        .children(1, &[2, 3])
        .root(1)
        .viewport(100., 100.);
    b.op(8, &layout_payload(1, -1., -1., 1));
    b.op(
        16,
        &extended_layout_payload(1, [0.; 4], [-1.; 5], 1, 0, [-1.; 4]),
    );
    for id in [2, 3] {
        b.op(
            16,
            &extended_layout_payload(id, [0.; 4], [-1., -1., -1., -1., 60.], 0, 2, [-1.; 4]),
        );
    }
    e.flush(&b.bytes()).unwrap();
    assert_eq!(e.frame(2).unwrap().width, 60.);
    assert_eq!(e.frame(3).unwrap().x, 0.);
    assert!(e.frame(3).unwrap().y > e.frame(2).unwrap().y);
}

fn visual_payload(id: u32, opacity: f32) -> Vec<u8> {
    let mut p = id.to_le_bytes().to_vec();
    p.extend(opacity.to_le_bytes());
    p.extend(2f32.to_le_bytes());
    p.extend([255, 0, 0, 255]);
    p.extend([0, 0, 0, 128]);
    for value in [4f32, -2., 3., 5., 6., 1., 1., 30.] {
        p.extend(value.to_le_bytes());
    }
    p.push(0);
    p
}
fn typography_payload(id: u32, weight: u16) -> Vec<u8> {
    let mut p = id.to_le_bytes().to_vec();
    p.extend(weight.to_le_bytes());
    p.extend([1, 2]);
    p.extend(2i32.to_le_bytes());
    p.push(5);
    p.extend(1f32.to_le_bytes());
    p.extend(30f32.to_le_bytes());
    p.push(3);
    p.extend(4u32.to_le_bytes());
    p.extend(b"Test");
    p
}
#[test]
fn visual_styles_are_transactional_and_do_not_relayout() {
    let mut e = engine();
    e.flush(&initial()).unwrap();
    let frame = e.frame(2).unwrap();
    let passes = e.layout_passes;
    e.flush(&Batch::default().op(17, &visual_payload(2, 0.5)).bytes())
        .unwrap();
    assert_eq!(e.frame(2).unwrap(), frame);
    assert_eq!(e.layout_passes, passes);
    assert_eq!(e.node(2).unwrap().visual.opacity, 0.5);
    for opacity in [-1., 1.01, f32::NAN, f32::INFINITY] {
        assert!(e
            .flush(&Batch::default().op(17, &visual_payload(2, opacity)).bytes())
            .is_err());
        assert_eq!(e.node(2).unwrap().visual.opacity, 0.5);
    }
    let mut invalid = visual_payload(2, 1.);
    invalid[52] = 2;
    assert!(e.flush(&Batch::default().op(17, &invalid).bytes()).is_err());
    for end in 0..53 {
        assert!(parse(
            &Batch::default()
                .op(17, &visual_payload(2, 1.)[..end])
                .bytes()
        )
        .is_err());
    }
}
#[test]
fn typography_changes_invalidate_measurement_and_validate_wire() {
    struct FontMeasure;
    impl Measure for FontMeasure {
        fn measure(
            &mut self,
            n: &Node,
            _: Size<Option<f32>>,
            _: Size<AvailableSpace>,
        ) -> Result<Size<f32>> {
            Ok(Size {
                width: 40.,
                height: n.typography.weight as f32 / 10.,
            })
        }
    }
    let mut e = Engine::new(Box::new(FontMeasure));
    e.flush(&initial()).unwrap();
    let before = e.frame(2).unwrap().height;
    e.flush(&Batch::default().op(18, &typography_payload(2, 700)).bytes())
        .unwrap();
    assert!(e.frame(2).unwrap().height > before);
    assert_eq!(&*e.node(2).unwrap().typography.family, "Test");
    assert_eq!(e.node(2).unwrap().typography.decoration, 3);
    for weight in [0, 450, 1000] {
        assert!(e
            .flush(
                &Batch::default()
                    .op(18, &typography_payload(2, weight))
                    .bytes()
            )
            .is_err());
        assert_eq!(e.node(2).unwrap().typography.weight, 700);
    }
    let mut invalid = typography_payload(2, 400);
    invalid[21] = 4;
    assert!(parse(&Batch::default().op(18, &invalid).bytes()).is_err());
    for end in 0..30 {
        assert!(parse(
            &Batch::default()
                .op(18, &typography_payload(2, 400)[..end])
                .bytes()
        )
        .is_err());
    }
}
#[test]
fn platform_style_validation_precedes_presentation_commit() {
    struct LimitedMeasure;
    impl Measure for LimitedMeasure {
        fn validate_style(&self, node: &Node) -> Result<()> {
            if node.visual.rotation != 0. {
                Err(Error::Measurement("unsupported test style"))
            } else {
                Ok(())
            }
        }
        fn measure(
            &mut self,
            n: &Node,
            k: Size<Option<f32>>,
            a: Size<AvailableSpace>,
        ) -> Result<Size<f32>> {
            FixtureMeasure.measure(n, k, a)
        }
    }
    let mut e = Engine::new(Box::new(LimitedMeasure));
    e.flush(&initial()).unwrap();
    assert!(e
        .flush(&Batch::default().op(17, &visual_payload(2, 0.5)).bytes())
        .is_err());
    assert_eq!(e.node(2).unwrap().visual.opacity, 1.);
}

fn behavior_payload(
    id: u32,
    width: f32,
    ratio: f32,
    position: u8,
    visibility: u8,
    mask: u8,
    insets: [f32; 4],
) -> Vec<u8> {
    let mut p = id.to_le_bytes().to_vec();
    for v in [width, -1., ratio] {
        p.extend(v.to_le_bytes());
    }
    p.extend([position, visibility, mask, 0]);
    for v in insets {
        p.extend(v.to_le_bytes());
    }
    p
}
#[test]
fn percentage_absolute_aspect_ratio_and_visibility_affect_layout_and_reset() {
    let mut e = engine();
    let mut b = Batch::default();
    b.create(1, 1)
        .create(2, 1)
        .create(3, 1)
        .children(1, &[2, 3])
        .root(1)
        .viewport(200., 300.);
    b.op(8, &layout_payload(3, 30., 30., 0));
    b.op(
        16,
        &extended_layout_payload(2, [0.; 4], [-1.; 5], 0, 2, [-1.; 4]),
    );
    b.op(
        19,
        &behavior_payload(2, 50., 2., 1, 0, 3, [10., 20., 0., 0.]),
    );
    e.flush(&b.bytes()).unwrap();
    let f = e.frame(2).unwrap();
    assert_eq!((f.x, f.y, f.width, f.height), (10., 20., 100., 50.));
    assert_eq!(e.frame(3).unwrap().y, 0.);
    e.flush(
        &Batch::default()
            .op(19, &behavior_payload(2, 50., 2., 0, 1, 0, [0.; 4]))
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.frame(3).unwrap().y, 50.); // hidden retains its layout space
    e.flush(
        &Batch::default()
            .op(19, &behavior_payload(2, 50., 2., 0, 2, 0, [0.; 4]))
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.frame(2).unwrap().width, 0.);
    assert_eq!(e.frame(3).unwrap().y, 0.);
    e.flush(
        &Batch::default()
            .op(19, &behavior_payload(2, 50., 2., 0, 0, 0, [0.; 4]))
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.frame(3).unwrap().y, 50.);
    for p in [
        behavior_payload(2, -2., 2., 0, 0, 0, [0.; 4]),
        behavior_payload(2, 50., 0., 0, 0, 0, [0.; 4]),
        behavior_payload(2, 50., 2., 0, 3, 0, [0.; 4]),
        behavior_payload(2, 50., 2., 0, 0, 16, [0.; 4]),
        behavior_payload(2, 50., 2., 0, 0, 1, [f32::NAN; 4]),
    ] {
        assert!(e.flush(&Batch::default().op(19, &p).bytes()).is_err());
        assert_eq!(e.frame(3).unwrap().y, 50.);
    }
}

fn advanced_payload(id: u32, grid: bool, flow: u8) -> Vec<u8> {
    let mut p = vec![0; 56];
    p[0..4].copy_from_slice(&id.to_le_bytes());
    p[4] = flow;
    p[5] = 1;
    p[7] = grid as u8;
    p[8..12].copy_from_slice(&8f32.to_le_bytes());
    p[12..16].copy_from_slice(&10f32.to_le_bytes());
    p[44..46].copy_from_slice(&1u16.to_le_bytes());
    p[48..50].copy_from_slice(&1u16.to_le_bytes());
    if grid {
        p[50..52].copy_from_slice(&2u16.to_le_bytes());
        for (unit, value) in [(1, 50f32), (3, 1f32)] {
            p.push(unit);
            p.extend(value.to_le_bytes());
        }
    }
    p
}
#[test]
fn grid_gaps_rtl_inheritance_and_reset_change_geometry() {
    let mut e = engine();
    let mut b = Batch::default();
    b.create(1, 1)
        .create(2, 2)
        .create(3, 2)
        .create(4, 2)
        .children(1, &[2, 3, 4])
        .root(1)
        .viewport(200., 100.);
    b.op(20, &advanced_payload(1, true, 1));
    e.flush(&b.bytes()).unwrap();
    assert_eq!(
        (e.frame(2).unwrap().x, e.frame(2).unwrap().width),
        (0., 50.)
    );
    assert_eq!(
        (e.frame(3).unwrap().x, e.frame(3).unwrap().width),
        (60., 140.)
    );
    assert_eq!(e.frame(4).unwrap().y, 28.);
    let mut rtl = advanced_payload(1, true, 2);
    rtl[24..28].copy_from_slice(&12f32.to_le_bytes());
    rtl[28..32].copy_from_slice(&4f32.to_le_bytes());
    rtl[40] = 12;
    e.flush(&Batch::default().op(20, &rtl).bytes()).unwrap();
    assert_eq!(e.node(2).unwrap().resolved_flow, 2);
    assert_eq!(e.node(1).unwrap().padding(), [4., 0., 12., 0.]);
    assert_eq!(e.frame(2).unwrap().x, 138.);
    let mut reset = advanced_payload(1, false, 0);
    reset[8..16].copy_from_slice(&[0; 8]);
    e.flush(&Batch::default().op(20, &reset).bytes()).unwrap();
    assert_eq!(e.node(2).unwrap().resolved_flow, 1);
    assert_eq!(e.frame(2).unwrap().width, 200.);
    assert_eq!(e.frame(3).unwrap().y, 20.);
    for index in [4, 5, 6, 7, 40, 41, 54] {
        let mut bad = reset.clone();
        bad[index] = 255;
        assert!(e.flush(&Batch::default().op(20, &bad).bytes()).is_err());
        assert_eq!(e.frame(2).unwrap().width, 200.);
    }
}
fn gradient_payload(id: u32, kind: u8) -> Vec<u8> {
    let mut p = id.to_le_bytes().to_vec();
    p.extend([kind, if kind == 0 { 0 } else { 2 }]);
    for v in [0f32, 0., 1., 0.] {
        p.extend(v.to_le_bytes());
    }
    if kind != 0 {
        p.extend(0f32.to_le_bytes());
        p.extend([255, 0, 0, 255]);
        p.extend(1f32.to_le_bytes());
        p.extend([0, 0, 255, 255]);
    }
    p
}
#[test]
fn gradient_parser_interpolation_reset_and_atomic_rejection() {
    let mut e = engine();
    e.flush(&initial()).unwrap();
    let frame = e.frame(2).unwrap();
    e.flush(&Batch::default().op(21, &gradient_payload(1, 1)).bytes())
        .unwrap();
    let g = e.node(1).unwrap().gradient.as_ref().unwrap();
    assert_eq!(g.sample(50., 0., 100., 100.), [128, 0, 128, 255]);
    assert_eq!(g.sample(0., 0., 100., 100.), [255, 0, 0, 255]);
    assert_eq!(e.frame(2).unwrap(), frame);
    for (index, value) in [(4, 4), (5, 1)] {
        let mut p = gradient_payload(1, 1);
        p[index] = value;
        assert!(e.flush(&Batch::default().op(21, &p).bytes()).is_err());
        assert!(e.node(1).unwrap().gradient.is_some());
    }
    let mut p = gradient_payload(1, 1);
    p[30..34].copy_from_slice(&0f32.to_le_bytes());
    assert!(parse(&Batch::default().op(21, &p).bytes()).is_err());
    e.flush(&Batch::default().op(21, &gradient_payload(1, 0)).bytes())
        .unwrap();
    assert!(e.node(1).unwrap().gradient.is_none());
}

#[test]
fn sibling_z_order_is_stable_and_skips_layout() {
    let mut e = engine();
    e.flush(&initial()).unwrap();
    let passes = e.layout_passes;
    let original = e.frame(2).unwrap();
    let z = |id: u32, index: i32| {
        let mut p = id.to_le_bytes().to_vec();
        p.extend(index.to_le_bytes());
        p
    };
    for (index, expected) in [
        (1, vec![3, 2]),
        (i32::MAX, vec![3, 2]),
        (i32::MIN, vec![2, 3]),
        (0, vec![2, 3]),
    ] {
        e.flush(&Batch::default().op(22, &z(2, index)).bytes())
            .unwrap();
        assert_eq!(e.node(1).unwrap().paint_children, expected);
        assert_eq!(e.node(1).unwrap().children, vec![2, 3]);
        assert_eq!(e.frame(2).unwrap(), original);
        assert_eq!(e.layout_passes, passes);
    }
    e.flush(&Batch::default().children(1, &[3, 2]).bytes())
        .unwrap();
    assert_eq!(e.node(1).unwrap().paint_children, vec![3, 2]);
    assert_eq!(e.layout_passes, passes + 1);
    let before = e.node(1).unwrap().paint_children.clone();
    assert!(e
        .flush(&Batch::default().op(22, &z(99, 1)).bytes())
        .is_err());
    assert_eq!(e.node(1).unwrap().paint_children, before);
}

#[test]
fn image_presentation_updates_reset_without_measurement_and_reject_invalid_targets() {
    let mut e = engine();
    e.flush(
        &Batch::default()
            .create(1, 1)
            .create(2, 8)
            .children(1, &[2])
            .root(1)
            .viewport(100., 100.)
            .bytes(),
    )
    .unwrap();
    let passes = e.layout_passes;
    for fit in [1, 2, 3, 4, 0] {
        let mut p = 2u32.to_le_bytes().to_vec();
        p.push(fit);
        e.flush(&Batch::default().op(23, &p).bytes()).unwrap();
        assert_eq!(e.node(2).unwrap().image_fit, fit);
        assert_eq!(e.layout_passes, passes);
    }
    let mut tint = 2u32.to_le_bytes().to_vec();
    tint.extend([1, 10, 20, 30, 128]);
    e.flush(&Batch::default().op(24, &tint).bytes()).unwrap();
    assert_eq!(e.node(2).unwrap().image_tint, Some([10, 20, 30, 128]));
    tint[4] = 0;
    e.flush(&Batch::default().op(24, &tint).bytes()).unwrap();
    assert_eq!(e.node(2).unwrap().image_tint, None);
    assert_eq!(e.layout_passes, passes);
    tint[4] = 2;
    assert!(e.flush(&Batch::default().op(24, &tint).bytes()).is_err());
    assert!(e
        .flush(&Batch::default().op(23, &[2, 0, 0, 0, 5]).bytes())
        .is_err());
    assert!(e
        .flush(&Batch::default().op(23, &[1, 0, 0, 0, 1]).bytes())
        .is_err());
    assert!(e
        .flush(
            &Batch::default()
                .op(24, &[1, 0, 0, 0, 1, 255, 0, 0, 255])
                .bytes()
        )
        .is_err());
    assert_eq!(e.node(2).unwrap().image_fit, 0);
}

#[test]
fn image_fit_centers_crop_scale_and_no_upscale_modes() {
    use crate::image_fit::rect;
    assert_eq!(rect(0, [200., 100.], [100., 100.]), [0., 25., 100., 50.]);
    assert_eq!(rect(1, [200., 100.], [100., 100.]), [-50., 0., 200., 100.]);
    assert_eq!(rect(2, [200., 100.], [100., 100.]), [0., 0., 100., 100.]);
    assert_eq!(rect(3, [20., 10.], [100., 100.]), [40., 45., 20., 10.]);
    assert_eq!(rect(4, [20., 10.], [100., 100.]), [40., 45., 20., 10.]);
    assert_eq!(rect(4, [200., 100.], [100., 100.]), [0., 25., 100., 50.]);
    assert_eq!(rect(1, [100., 200.], [100., 100.]), [0., -50., 100., 200.]);
    assert_eq!(rect(0, [0., 100.], [100., 100.]), [0.; 4]);
}

#[test]
fn background_assets_validate_before_presentation_commit_without_reflow() {
    struct AssetMeasure(std::rc::Rc<std::cell::Cell<usize>>);
    impl Measure for AssetMeasure {
        fn measure(
            &mut self,
            node: &Node,
            known: Size<Option<f32>>,
            available: Size<AvailableSpace>,
        ) -> Result<Size<f32>> {
            if node.kind == Kind::Image {
                self.0.set(self.0.get() + 1);
                if &*node.image == b"bad" {
                    return Err(Error::Measurement("invalid background asset"));
                }
            }
            FixtureMeasure.measure(node, known, available)
        }
    }
    let calls = std::rc::Rc::new(std::cell::Cell::new(0));
    let mut e = Engine::new(Box::new(AssetMeasure(calls.clone())));
    e.flush(&initial()).unwrap();
    let passes = e.layout_passes;
    let payload = |id: u32, fit: u8, data: &[u8]| {
        let mut p = id.to_le_bytes().to_vec();
        p.push(fit);
        p.extend((data.len() as u32).to_le_bytes());
        p.extend(data);
        p
    };
    e.flush(&Batch::default().op(25, &payload(1, 1, b"valid")).bytes())
        .unwrap();
    assert_eq!(calls.get(), 1);
    assert_eq!(e.layout_passes, passes);
    e.flush(&Batch::default().op(25, &payload(1, 0, b"valid")).bytes())
        .unwrap();
    assert_eq!(calls.get(), 1);
    assert_eq!(e.node(1).unwrap().background_fit, 0);
    assert!(e
        .flush(&Batch::default().op(25, &payload(1, 1, b"bad")).bytes())
        .is_err());
    assert_eq!(&*e.node(1).unwrap().background_image, b"valid");
    assert_eq!(e.node(1).unwrap().background_fit, 0);
    assert!(e
        .flush(&Batch::default().op(25, &payload(2, 1, b"valid")).bytes())
        .is_err());
    assert!(e
        .flush(&Batch::default().op(25, &payload(1, 5, b"valid")).bytes())
        .is_err());
    e.flush(&Batch::default().op(25, &payload(1, 1, b"")).bytes())
        .unwrap();
    assert!(e.node(1).unwrap().background_image.is_empty());
    assert_eq!(e.layout_passes, passes);
}

#[test]
fn box_decoration_is_transactional_and_presentation_only() {
    let mut e = Engine::new(Box::new(FixtureMeasure));
    e.flush(&initial()).unwrap();
    let passes = e.layout_passes;
    let mut p = 1u32.to_le_bytes().to_vec();
    p.push(31);
    for r in [24f32, 0., 12., 4.] {
        p.extend(r.to_le_bytes());
    }
    for i in 0..4 {
        p.extend(3f32.to_le_bytes());
        p.extend([255, 0, 0, 255, i % 3]);
    }
    e.flush(&Batch::default().op(26, &p).bytes()).unwrap();
    let expected = e.node(1).unwrap().decoration;
    assert_eq!(expected.corners, Some([24., 0., 12., 4.]));
    for (offset, bytes) in [
        (4, vec![32]),
        (29, vec![3]),
        (5, f32::NAN.to_le_bytes().to_vec()),
        (21, (-1f32).to_le_bytes().to_vec()),
    ] {
        let mut bad = p.clone();
        bad[offset..offset + bytes.len()].copy_from_slice(&bytes);
        assert!(e.flush(&Batch::default().op(26, &bad).bytes()).is_err());
        assert_eq!(e.node(1).unwrap().decoration, expected);
    }
    p[4..].fill(0);
    e.flush(&Batch::default().op(26, &p).bytes()).unwrap();
    assert_eq!(e.node(1).unwrap().decoration, Default::default());
    assert_eq!(e.layout_passes, passes);
}

#[test]
fn enabled_updates_are_transactional_and_skip_layout() {
    let mut e = Engine::new(Box::new(FixtureMeasure));
    e.flush(&Batch::default().create(1, 3).root(1).bytes())
        .unwrap();
    let passes = e.layout_passes;
    for enabled in [0, 1] {
        e.flush(&Batch::default().op(27, &[1, 0, 0, 0, enabled]).bytes())
            .unwrap();
        assert_eq!(e.node(1).unwrap().enabled, enabled == 1);
    }
    assert!(e
        .flush(&Batch::default().op(27, &[1, 0, 0, 0, 2]).bytes())
        .is_err());
    assert!(e.node(1).unwrap().enabled);
    assert_eq!(e.layout_passes, passes);
    assert!(e
        .flush(
            &Batch::default()
                .create(2, 2)
                .op(27, &[2, 0, 0, 0, 0])
                .bytes()
        )
        .is_err());
    assert!(e.node(2).is_none());
}

#[test]
fn rich_text_runs_validate_text_and_invalidate_measurement() {
    let mut e = Engine::new(Box::new(FixtureMeasure));
    e.flush(
        &Batch::default()
            .create(1, 2)
            .text(1, "Ž 🙂")
            .root(1)
            .bytes(),
    )
    .unwrap();
    let passes = e.layout_passes;
    let mut p = 1u32.to_le_bytes().to_vec();
    p.extend(1u32.to_le_bytes());
    p.extend(("Ž 🙂".len() as u32).to_le_bytes());
    p.extend("Ž 🙂".as_bytes());
    p.push(1);
    p.extend(30f32.to_le_bytes());
    p.extend([255, 0, 0, 255]);
    p.extend(400u16.to_le_bytes());
    p.extend([0, 0]);
    p.extend(0u32.to_le_bytes());
    e.flush(&Batch::default().op(28, &p).bytes()).unwrap();
    assert!(e.layout_passes > passes);
    let n = e.node(1).unwrap();
    assert_eq!(n.run_style(&n.text_runs[0]).style.font_size, 30.);
    assert!(e
        .flush(&Batch::default().text(1, "different").bytes())
        .is_err());
    assert_eq!(&*e.node(1).unwrap().text, "Ž 🙂");
    let mut bad = p.clone();
    bad[12 + "Ž 🙂".len()] = 255;
    assert!(e.flush(&Batch::default().op(28, &bad).bytes()).is_err());
    let mut clear = 1u32.to_le_bytes().to_vec();
    clear.extend(0u32.to_le_bytes());
    e.flush(&Batch::default().op(28, &clear).text(1, "plain").bytes())
        .unwrap();
    assert!(e.node(1).unwrap().text_runs.is_empty());
}

#[test]
fn font_assets_and_axes_validate_invalidate_and_reset_transactionally() {
    let data = include_bytes!("../tests/fixtures/DotNativeTest.ttf");
    assert!(crate::font_assets::FontAsset::new("DotNativeTest", data).is_ok());
    assert!(crate::font_assets::FontAsset::new("Wrong", data).is_err());
    // SFNT permits trailing alignment bytes after the final table.
    let table_count = u16::from_be_bytes(data[4..6].try_into().unwrap()) as usize;
    let used = (0..table_count)
        .map(|i| {
            let p = 12 + i * 16;
            u32::from_be_bytes(data[p + 8..p + 12].try_into().unwrap()) as usize
                + u32::from_be_bytes(data[p + 12..p + 16].try_into().unwrap()) as usize
        })
        .max()
        .unwrap();
    for end in 0..used {
        assert!(crate::font_assets::FontAsset::new("DotNativeTest", &data[..end]).is_err());
    }
    let mut e = engine();
    e.flush(&initial()).unwrap();
    let mut axis = 2u32.to_le_bytes().to_vec();
    axis.push(1);
    axis.extend(b"wdth");
    axis.extend(150f32.to_le_bytes());
    let passes = e.layout_passes;
    e.flush(&Batch::default().op(29, &axis).bytes()).unwrap();
    assert!(e.layout_passes > passes);
    assert_eq!(&*e.node(2).unwrap().font_axes, &[(*b"wdth", 150.)]);
    for value in [f32::NAN, f32::INFINITY] {
        axis[9..13].copy_from_slice(&value.to_le_bytes());
        assert!(e.flush(&Batch::default().op(29, &axis).bytes()).is_err());
        assert_eq!(e.node(2).unwrap().font_axes[0].1, 150.);
    }
    axis[9..13].copy_from_slice(&150f32.to_le_bytes());
    axis[4] = 2;
    axis.extend_from_within(5..13);
    assert!(e.flush(&Batch::default().op(29, &axis).bytes()).is_err());
    let mut asset = 2u32.to_le_bytes().to_vec();
    asset.extend(13u32.to_le_bytes());
    asset.extend(b"DotNativeTest");
    asset.extend((data.len() as u32).to_le_bytes());
    asset.extend(data);
    assert!(e.flush(&Batch::default().op(30, &asset).bytes()).is_err()); // wrong style family
    assert!(e.node(2).unwrap().font_asset.is_none());
    let mut typography = typography_payload(2, 400);
    typography.truncate(22);
    typography.extend(13u32.to_le_bytes());
    typography.extend(b"DotNativeTest");
    e.flush(&Batch::default().op(18, &typography).op(30, &asset).bytes())
        .unwrap();
    assert!(e.node(2).unwrap().font_asset.is_some());
    assert!(e.node(3).unwrap().font_revision > 0); // other family users must remeasure too
    e.flush(
        &Batch::default()
            .create(4, 2)
            .text(4, "new font user")
            .children(1, &[2, 3, 4])
            .bytes(),
    )
    .unwrap();
    assert_eq!(
        e.node(4).unwrap().font_revision,
        e.node(2).unwrap().font_revision
    );
    let mut reset = 2u32.to_le_bytes().to_vec();
    reset.extend([0; 8]);
    e.flush(
        &Batch::default()
            .op(30, &reset)
            .op(29, &[2, 0, 0, 0, 0])
            .bytes(),
    )
    .unwrap();
    assert!(e.node(2).unwrap().font_asset.is_none());
    assert!(e.node(2).unwrap().font_axes.is_empty());
}

#[test]
fn font_registration_is_deduplicated_and_failed_registration_can_retry() {
    let mut e = engine();
    e.flush(&initial()).unwrap();
    let mut n = e.node(2).unwrap().clone();
    n.font_asset = Some(
        crate::font_assets::FontAsset::new(
            "DotNativeTest",
            include_bytes!("../tests/fixtures/DotNativeTest.ttf"),
        )
        .unwrap(),
    );
    assert!(
        crate::font_assets::ensure(&n, |_| Err(Error::Measurement("fixture failure"))).is_err()
    );
    crate::font_assets::ensure(&n, |_| Ok(Box::new(()))).unwrap();
    crate::font_assets::ensure(&n, |_| panic!("same font registered twice")).unwrap();
    let asset = n.font_asset.as_mut().unwrap();
    let mut changed = asset.data.to_vec();
    changed[8] ^= 1;
    asset.data = changed.into();
    assert!(crate::font_assets::ensure(&n, |_| panic!(
        "family conflict reached native registration"
    ))
    .is_err());
}

#[test]
fn font_family_registers_distinct_faces_but_rejects_ambiguous_styles() {
    let mut e = engine();
    e.flush(&initial()).unwrap();
    let mut node = e.node(2).unwrap().clone();
    let regular = crate::font_assets::FontAsset::new(
        "DotNativeTest",
        include_bytes!("../tests/fixtures/DotNativeTest.ttf"),
    )
    .unwrap();
    let bold = crate::font_assets::FontAsset::new(
        "DotNativeTest",
        include_bytes!("../tests/fixtures/DotNativeTest-Bold.ttf"),
    )
    .unwrap();
    assert_eq!(regular.weight, 400);
    assert_eq!(bold.weight, 700);
    assert_ne!(regular.face, bold.face);
    node.font_asset = Some(regular.clone());
    crate::font_assets::ensure(&node, |_| Ok(Box::new(()))).unwrap();
    node.font_asset = Some(bold.clone());
    crate::font_assets::ensure(&node, |_| Ok(Box::new(()))).unwrap();
    let mut conflict = regular.clone();
    conflict.face = "Another-Regular".into();
    node.font_asset = Some(conflict);
    assert!(crate::font_assets::ensure(&node, |_| panic!("ambiguous style registered")).is_err());
    let mut alias = bold;
    alias.family = "Another family".into();
    node.font_asset = Some(alias);
    assert!(crate::font_assets::ensure(&node, |_| panic!("alias registered")).is_err());
}

#[test]
fn bottom_navigation_validates_wire_and_rolls_back_invalid_updates() {
    fn payload(selected: u32, count: u32, icon: u32) -> Vec<u8> {
        let mut p = 2u32.to_le_bytes().to_vec();
        p.extend(selected.to_le_bytes());
        p.extend(count.to_le_bytes());
        for label in ["Home", "Profil 🙂"] {
            p.extend(icon.to_le_bytes());
            p.extend((label.len() as u32).to_le_bytes());
            p.extend(label.as_bytes());
        }
        p
    }
    let mut e = engine();
    let good = payload(0, 2, 0);
    e.flush(
        &Batch::default()
            .viewport(200., 300.)
            .create(1, 1)
            .create(2, 14)
            .op(32, &good)
            .children(1, &[2])
            .root(1)
            .bytes(),
    )
    .unwrap();
    assert_eq!(
        e.node(2).unwrap().bottom_navigation.items[1].1.as_ref(),
        "Profil 🙂"
    );
    for bad in [
        payload(2, 2, 0),
        payload(0, 6, 0),
        payload(0, 2, 5),
        good[..good.len() - 1].to_vec(),
    ] {
        assert!(e.flush(&Batch::default().op(32, &bad).bytes()).is_err());
        assert_eq!(e.node(2).unwrap().bottom_navigation.selected, 0);
    }
    e.flush(&Batch::default().op(32, &payload(1, 2, 0)).bytes())
        .unwrap();
    assert_eq!(e.node(2).unwrap().bottom_navigation.selected, 1);
    e.flush(&Batch::default().op(27, &[2, 0, 0, 0, 0]).bytes())
        .unwrap();
    assert!(!e.node(2).unwrap().enabled);
    e.flush(&Batch::default().op(27, &[2, 0, 0, 0, 1]).bytes())
        .unwrap();
    assert!(e.node(2).unwrap().enabled);
    assert!(e.flush(&Batch::default().create(3, 14).bytes()).is_err());
    assert!(e.node(3).is_none());
    let mut wrong = payload(0, 2, 0);
    wrong[..4].copy_from_slice(&1u32.to_le_bytes());
    assert!(e.flush(&Batch::default().op(32, &wrong).bytes()).is_err());
}

#[test]
fn bottom_safe_area_is_reserved_once_and_owned_by_native_tabs() {
    struct InsetMeasure;
    impl Measure for InsetMeasure {
        fn bottom_safe_area(&self) -> f32 {
            34.
        }
        fn measure(
            &mut self,
            node: &Node,
            known: Size<Option<f32>>,
            available: Size<AvailableSpace>,
        ) -> Result<Size<f32>> {
            FixtureMeasure.measure(node, known, available)
        }
    }
    let mut e = Engine::new(Box::new(InsetMeasure));
    e.flush(
        &Batch::default()
            .viewport(402., 812.)
            .create(1, 1)
            .create(3, 1)
            .children(1, &[3])
            .root(1)
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.frame(1).unwrap().height, 778.);
    let mut tabs = Vec::new();
    for value in [4u32, 0, 2, 0, 1] {
        tabs.extend(value.to_le_bytes());
    }
    tabs.push(b'A');
    tabs.extend(1u32.to_le_bytes());
    tabs.extend(1u32.to_le_bytes());
    tabs.push(b'B');
    e.flush(
        &Batch::default()
            .create(4, 14)
            .op(32, &tabs)
            .children(3, &[4])
            .bytes(),
    )
    .unwrap();
    assert_eq!(e.frame(1).unwrap().height, 812.);
    e.flush(&Batch::default().children(3, &[]).delete(4).bytes())
        .unwrap();
    assert_eq!(e.frame(1).unwrap().height, 778.);
}

#[test]
fn desktop_sidebar_validates_configuration_and_rolls_back() {
    fn sidebar(selected: i32, width: f32) -> Vec<u8> {
        let mut p = 1u32.to_le_bytes().to_vec();
        p.extend(selected.to_le_bytes());
        p.extend(1u32.to_le_bytes());
        p.extend(width.to_le_bytes());
        p.extend(1u32.to_le_bytes());
        p.extend(0u32.to_le_bytes());
        for text in ["Navigation", "Home", "⌂"] {
            p.extend((text.len() as u32).to_le_bytes());
            p.extend(text.as_bytes());
        }
        p
    }
    let mut e = engine();
    let initial = Batch::default()
        .viewport(800., 600.)
        .create(1, 15)
        .create(2, 1)
        .children(1, &[2])
        .op(33, &sidebar(0, 260.))
        .root(1)
        .bytes();
    e.flush(&initial).unwrap();
    let mut invalid_theme = sidebar(0, 260.);
    invalid_theme[20..24].copy_from_slice(&3u32.to_le_bytes());
    for packet in [
        invalid_theme,
        sidebar(1, 260.),
        sidebar(-2, 260.),
        sidebar(0, f32::NAN),
        sidebar(0, 179.),
    ] {
        assert!(e.flush(&Batch::default().op(33, &packet).bytes()).is_err());
        assert_eq!(e.node(1).unwrap().desktop_navigation.width, 260.);
        assert_eq!(e.node(1).unwrap().desktop_navigation.selected, 0);
    }
    assert!(e
        .flush(&Batch::default().children(1, &[]).delete(2).bytes())
        .is_err());
    assert!(e.node(2).is_some());
    assert!(e
        .flush(&Batch::default().op(33, &sidebar(-1, 300.)).bytes())
        .is_ok());
}

#[test]
fn text_input_validates_resets_and_rolls_back() {
    let mut engine = Engine::new(Box::new(FixtureMeasure));
    let mut mount = Batch::default();
    mount.create(1, 1).create(2, 5).children(1, &[2]).root(1);
    mount.op(34, &[2, 0, 0, 0, 1, 2, 0, 4, 4]);
    engine.flush(&mount.bytes()).unwrap();
    assert_eq!(engine.node(2).unwrap().text_input.keyboard, 1);
    let tinted = [2, 0, 0, 0, 0, 0, 0, 0, 4, 197, 192, 204, 255];
    engine
        .flush(&Batch::default().op(34, &tinted).bytes())
        .unwrap();
    assert_eq!(
        engine.node(2).unwrap().text_input.placeholder_color,
        Some([197, 192, 204, 255])
    );
    for length in 10..13 {
        assert!(engine
            .flush(&Batch::default().op(34, &tinted[..length]).bytes())
            .is_err());
        assert_eq!(
            engine.node(2).unwrap().text_input.placeholder_color,
            Some([197, 192, 204, 255])
        );
    }
    let reset = [2, 0, 0, 0, 0, 0, 0, 0, 4];
    engine
        .flush(&Batch::default().op(34, &reset).bytes())
        .unwrap();
    assert_eq!(
        engine.node(2).unwrap().text_input,
        protocol::TextInputOptions::default()
    );
    for (offset, value) in [(4, 6), (5, 6), (6, 4), (7, 6), (8, 8), (8, 3)] {
        let mut invalid = reset;
        invalid[offset] = value;
        let mut batch = Batch::default();
        batch.text(2, "must roll back").op(34, &invalid);
        assert!(engine.flush(&batch.bytes()).is_err());
        assert_eq!(&*engine.node(2).unwrap().text, "");
    }
    assert!(engine
        .flush(
            &Batch::default()
                .op(34, &[1, 0, 0, 0, 0, 0, 0, 0, 4])
                .bytes()
        )
        .is_err());
    assert!(engine
        .flush(&Batch::default().op(34, &reset[..8]).bytes())
        .is_err());
    // In-place changes would leave an existing UITextField paired with UITextView properties.
    assert!(engine
        .flush(
            &Batch::default()
                .op(34, &[2, 0, 0, 0, 0, 0, 0, 0, 6])
                .bytes()
        )
        .is_err());
    let mut multiline = Batch::default();
    multiline
        .create(3, 5)
        .op(34, &[3, 0, 0, 0, 0, 4, 0, 0, 6])
        .children(1, &[3])
        .delete(2);
    engine.flush(&multiline.bytes()).unwrap();
    assert!(engine.node(3).unwrap().text_input.multiline());
}

#[test]
fn scroll_requests_validate_atomically_without_layout() {
    let mut engine = Engine::new(Box::new(FixtureMeasure));
    let mut mount = Batch::default();
    mount.create(1, 4).create(2, 1).children(1, &[2]).root(1);
    engine.flush(&mount.bytes()).unwrap();
    let mut request = 1u32.to_le_bytes().to_vec();
    request.extend(3u32.to_le_bytes());
    request.extend(120f32.to_le_bytes());
    request.push(1);
    engine
        .flush(&Batch::default().op(35, &request).bytes())
        .unwrap();
    assert_eq!(engine.node(1).unwrap().scroll_position, (3, 120., true));
    let mut invalid = request.clone();
    invalid[12] = 2;
    assert!(engine
        .flush(&Batch::default().op(35, &invalid).bytes())
        .is_err());
    invalid = request.clone();
    invalid[8..12].copy_from_slice(&f32::NAN.to_le_bytes());
    assert!(engine
        .flush(&Batch::default().op(35, &invalid).bytes())
        .is_err());
    invalid = request.clone();
    invalid[..4].copy_from_slice(&2u32.to_le_bytes());
    assert!(engine
        .flush(&Batch::default().op(35, &invalid).bytes())
        .is_err());
    assert!(engine
        .flush(&Batch::default().op(35, &request[..12]).bytes())
        .is_err());
    assert_eq!(engine.node(1).unwrap().scroll_position, (3, 120., true));
}

#[test]
fn measured_layout_tracks_content_width_revision_and_failed_batches() {
    fn poll() -> Vec<u8> {
        // Tests run on worker threads; avoid the FFI entry point's process UI-thread binding.
        let Some((id, kind, value, text)) = crate::events::take_for_test() else {
            return vec![];
        };
        let mut bytes = id.to_le_bytes().to_vec();
        bytes.extend(kind.to_le_bytes());
        bytes.extend(value.to_le_bytes());
        bytes.extend((text.len() as u32).to_le_bytes());
        bytes.extend(text.as_bytes());
        bytes
    }
    while !poll().is_empty() {}
    let mut e = engine();
    e.flush(&initial()).unwrap();
    let observe = |revision: u32| [2u32.to_le_bytes(), revision.to_le_bytes()].concat();
    let passes = e.layout_passes;
    e.flush(&Batch::default().op(36, &observe(7)).bytes())
        .unwrap();
    assert_eq!(e.layout_passes, passes);
    e.report_layouts().unwrap();
    let event = poll();
    assert_eq!(u32::from_le_bytes(event[4..8].try_into().unwrap()), 11);
    assert_eq!(f32::from_le_bytes(event[8..12].try_into().unwrap()), 20.);
    assert_eq!(std::str::from_utf8(&event[16..]).unwrap(), "7:180");
    e.report_layouts().unwrap();
    assert!(poll().is_empty());
    e.flush(&Batch::default().text(2, "tall").bytes()).unwrap();
    e.report_layouts().unwrap();
    assert_eq!(f32::from_le_bytes(poll()[8..12].try_into().unwrap()), 40.);
    e.flush(&Batch::default().viewport(100., 300.).bytes())
        .unwrap();
    e.report_layouts().unwrap();
    assert_eq!(std::str::from_utf8(&poll()[16..]).unwrap(), "7:80");
    assert!(e
        .flush(&Batch::default().op(36, &observe(8)).delete(99).bytes())
        .is_err());
    e.report_layouts().unwrap();
    assert!(poll().is_empty());
    e.flush(&Batch::default().op(36, &observe(8)).bytes())
        .unwrap();
    e.report_layouts().unwrap();
    assert_eq!(std::str::from_utf8(&poll()[16..]).unwrap(), "8:80");
    e.flush(&Batch::default().op(36, &observe(0)).bytes())
        .unwrap();
    e.report_layouts().unwrap();
    assert!(poll().is_empty());
}

#[test]
fn native_collection_backend_is_validated_and_cannot_change_in_place() {
    let mut e = engine();
    let frame = Batch::default()
        .viewport(300., 400.)
        .create(1, 4)
        .create(2, 1)
        .children(1, &[2])
        .root(1)
        .op(37, &1u32.to_le_bytes())
        .bytes();
    e.flush(&frame).unwrap();
    assert!(e.node(1).unwrap().virtual_list);
    let passes = e.layout_passes;
    e.flush(&Batch::default().op(37, &1u32.to_le_bytes()).bytes())
        .unwrap();
    assert_eq!(passes, e.layout_passes);
    assert!(e
        .flush(&Batch::default().op(37, &2u32.to_le_bytes()).bytes())
        .is_err());
    assert!(!e.node(2).unwrap().virtual_list);
    assert!(e.flush(&Batch::default().children(1, &[]).bytes()).is_err());
    assert_eq!(e.node(1).unwrap().children, vec![2]);
    let mut plain = engine();
    plain
        .flush(
            &Batch::default()
                .create(1, 4)
                .create(2, 1)
                .children(1, &[2])
                .root(1)
                .bytes(),
        )
        .unwrap();
    assert!(plain
        .flush(&Batch::default().op(37, &1u32.to_le_bytes()).bytes())
        .is_err());
    assert!(!plain.node(1).unwrap().virtual_list);
    let invalid = Batch::default()
        .create(1, 4)
        .create(2, 2)
        .children(1, &[2])
        .root(1)
        .op(37, &1u32.to_le_bytes())
        .bytes();
    assert!(engine().flush(&invalid).is_err());
}

#[test]
fn row_recycling_requires_complete_compatible_inactive_subtrees() {
    let mut e = engine();
    let mut b = Batch::default();
    b.create(1, 4)
        .create(2, 1)
        .create(10, 1)
        .create(11, 3)
        .children(1, &[2])
        .children(2, &[10])
        .children(10, &[11])
        .root(1)
        .viewport(400., 400.);
    b.op(37, &1u32.to_le_bytes());
    e.flush(&b.bytes()).unwrap();
    let previous = e.presentation_nodes();
    let mut b = Batch::default();
    b.create(20, 1)
        .create(21, 3)
        .children(20, &[21])
        .children(2, &[20])
        .children(10, &[])
        .delete(11)
        .delete(10);
    e.flush(&b.bytes()).unwrap();
    let plan = crate::platform::row_recycling::plan(&previous, &e);
    assert_eq!(plan, vec![(10, 20), (11, 21)]);
    crate::events::emit(11, 7, 1., String::new());
    assert!(crate::platform::row_recycling::plan(&previous, &e).is_empty());
    crate::events::emit(11, 7, 0., String::new());
    assert_eq!(crate::platform::row_recycling::plan(&previous, &e), plan);
    let mut incompatible = previous.clone();
    let mut field = (**incompatible.get(&11).unwrap()).clone();
    field.kind = Kind::TextField;
    incompatible.insert(11, std::sync::Arc::new(field));
    assert!(crate::platform::row_recycling::plan(&incompatible, &e).is_empty());
    // A surviving descendant cannot be stolen into another item's subtree.
    let mut overlapping = previous.clone();
    let mut root = (**overlapping.get(&10).unwrap()).clone();
    root.children = vec![21];
    overlapping.insert(10, std::sync::Arc::new(root));
    overlapping.insert(21, previous[&11].clone());
    assert!(crate::platform::row_recycling::plan(&overlapping, &e).is_empty());
    crate::events::retain_bindings(|_| false);
    while crate::events::take_for_test().is_some() {}
}

#[test]
fn platform_view_type_is_stored_and_rejected() {
    let mut session = engine();
    session
        .flush(
            &Batch::default()
                .viewport(200., 100.)
                .create(1, 16)
                .view_type(1, "webview")
                .root(1)
                .bytes(),
        )
        .unwrap();
    assert_eq!(&*session.node(1).unwrap().platform_view, "webview");
    let mut fresh = engine();
    fresh.flush(&initial()).unwrap();
    assert!(fresh
        .flush(&Batch::default().view_type(2, "webview").bytes())
        .is_err());
    assert_eq!(&*fresh.node(2).unwrap().text, "é\0🙂");
    assert!(parse(
        &Batch::default()
            .create(1, 16)
            .view_type(1, "no spaces")
            .root(1)
            .bytes()
    )
    .is_err());
}
