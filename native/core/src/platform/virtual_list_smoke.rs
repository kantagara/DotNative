//! Shared desktop presentation scenario; no synthetic native control objects.
#[derive(Default)]
pub(crate) struct Batch(Vec<(u8, Vec<u8>)>);
impl Batch {
    fn op(&mut self, code: u8, data: Vec<u8>) {
        self.0.push((code, data));
    }
    fn create(&mut self, id: u32, kind: u8) {
        let mut p = id.to_le_bytes().to_vec();
        p.push(kind);
        self.op(1, p);
    }
    fn children(&mut self, id: u32, rows: &[u32]) {
        let mut p = id.to_le_bytes().to_vec();
        p.extend((rows.len() as u32).to_le_bytes());
        for id in rows {
            p.extend(id.to_le_bytes());
        }
        self.op(4, p);
    }
    fn height(&mut self, id: u32, height: f32) {
        let mut p = id.to_le_bytes().to_vec();
        for value in [-1f32, height, 0., 0.] {
            p.extend(value.to_le_bytes());
        }
        p.extend([0, 0, 0, 0]);
        self.op(8, p);
    }
    fn scroll(&mut self, revision: u32, offset: f32) {
        let mut p = 1u32.to_le_bytes().to_vec();
        p.extend(revision.to_le_bytes());
        p.extend(offset.to_le_bytes());
        p.push(1);
        self.op(35, p);
    }
    fn window(&mut self, rows: &[u32]) {
        let mut children = vec![3];
        children.extend(rows);
        children.push(4);
        self.children(2, &children);
    }
    fn rows(&mut self, rows: &[u32]) {
        for id in rows {
            self.create(*id, 1);
            self.height(*id, 64.);
            self.create(id + 1, 3);
            self.height(id + 1, 48.);
            self.children(*id, &[id + 1]);
        }
    }
    fn delete_rows(&mut self, rows: &[u32]) {
        for id in rows {
            self.children(*id, &[]);
            self.op(6, (id + 1).to_le_bytes().to_vec());
            self.op(6, id.to_le_bytes().to_vec());
        }
    }
    fn bytes(self) -> Vec<u8> {
        let mut bytes = b"DNTV\x01\x00\x10\x00".to_vec();
        bytes.extend(0u32.to_le_bytes());
        bytes.extend((self.0.len() as u32).to_le_bytes());
        for (code, data) in self.0 {
            bytes.push(code);
            bytes.extend((data.len() as u32).to_le_bytes());
            bytes.extend(data);
        }
        let len = bytes.len() as u32;
        bytes[8..12].copy_from_slice(&len.to_le_bytes());
        bytes
    }
}
pub(crate) fn run(mut present: impl FnMut(&[u8], &[u32], f32) -> Vec<usize>) {
    let rows: Vec<_> = (10..34).step_by(2).collect();
    let mut b = Batch::default();
    b.create(1, 4);
    b.create(2, 1);
    b.create(3, 1);
    b.create(4, 1);
    b.create(5, 1);
    b.children(5, &[1]);
    b.op(37, 1u32.to_le_bytes().to_vec());
    b.height(1, 320.);
    b.height(3, 100_000.);
    b.height(4, 500_000.);
    b.children(1, &[2]);
    b.rows(&rows);
    b.window(&rows);
    b.op(5, 5u32.to_le_bytes().to_vec());
    b.op(
        7,
        [480f32, 640.]
            .into_iter()
            .flat_map(f32::to_le_bytes)
            .collect(),
    );
    b.scroll(1, 100_000.);
    let original = present(&b.bytes(), &rows, 100_000.);
    let next: Vec<_> = (100..124).step_by(2).collect();
    let mut b = Batch::default();
    b.rows(&next);
    b.window(&next);
    b.delete_rows(&rows);
    b.height(3, 300_000.);
    b.height(4, 300_000.);
    b.scroll(2, 300_000.);
    let reused = present(&b.bytes(), &next, 300_000.);
    assert_eq!(
        original
            .iter()
            .copied()
            .collect::<std::collections::HashSet<_>>(),
        reused.iter().copied().collect(),
        "distant window must reuse native containers"
    );
    let reversed: Vec<_> = next.iter().rev().copied().collect();
    for height in [180., 64.] {
        let mut b = Batch::default();
        b.height(110, height);
        b.window(&reversed);
        let stable = present(&b.bytes(), &reversed, 300_000.);
        assert_eq!(
            stable,
            reused.iter().rev().copied().collect::<Vec<_>>(),
            "reordering and resizing keep native identity"
        );
    }
    let mut b = Batch::default();
    b.window(&[]);
    b.delete_rows(&next);
    b.height(3, 0.);
    b.height(4, 0.);
    b.scroll(3, 0.);
    assert!(present(&b.bytes(), &[], 0.).is_empty());
}

pub(crate) fn expect_scroll(revision: u32, offset: f32) {
    let mut buffer = [0u8; 4096];
    let mut found = false;
    loop {
        let size =
            unsafe { crate::events::dotnative_poll_event(buffer.as_mut_ptr(), buffer.len()) };
        if size == 0 {
            break;
        }
        assert!(size <= buffer.len());
        let id = u32::from_le_bytes(buffer[..4].try_into().unwrap());
        let kind = u32::from_le_bytes(buffer[4..8].try_into().unwrap());
        if id == 1 && kind == 10 {
            found = true;
            assert!((f32::from_le_bytes(buffer[8..12].try_into().unwrap()) - offset).abs() < 1.);
            let text = std::str::from_utf8(&buffer[16..size]).unwrap();
            let (actual_revision, viewport) = text.split_once(':').unwrap();
            assert_eq!(actual_revision.parse::<u32>().unwrap(), revision);
            assert!(viewport.parse::<f32>().unwrap() > 0.);
        }
    }
    assert!(
        found,
        "native scrolling must report the current revision and viewport"
    );
}

/// Reused measurement must match a fresh native prototype after style changes.
pub(crate) fn check_measurement_reuse(
    mut make: impl FnMut() -> Box<dyn crate::Measure>,
    mut node: crate::Node,
) {
    use crate::{AvailableSpace, Kind, Size};
    let mut reused = make();
    for kind in [
        Kind::Text,
        Kind::Button,
        Kind::TextField,
        Kind::RadioButton,
        Kind::Switch,
        Kind::Slider,
    ] {
        node.kind = kind;
        for variant in 0..12 {
            node.text = if variant % 2 == 0 {
                "Long wrapping text repeated across a narrow measurement constraint"
            } else {
                "Short"
            }
            .into();
            node.style.font_size = if variant % 3 == 0 { 24. } else { 14. };
            node.typography.weight = if variant % 2 == 0 { 700 } else { 400 };
            node.enabled = variant % 2 == 0;
            for width in [40., 160., 400.] {
                let known = Size {
                    width: None,
                    height: None,
                };
                let available = Size {
                    width: AvailableSpace::Definite(width),
                    height: AvailableSpace::MaxContent,
                };
                let actual = reused.measure(&node, known, available).unwrap();
                let expected = make().measure(&node, known, available).unwrap();
                assert!((actual.width-expected.width).abs()<0.01 && (actual.height-expected.height).abs()<0.01,
                    "reused measurement differs: {kind:?} variant {variant}, width {width}: {actual:?} != {expected:?}");
            }
        }
    }
    println!("PASS 216 reused/fresh native measurement comparisons");
}
