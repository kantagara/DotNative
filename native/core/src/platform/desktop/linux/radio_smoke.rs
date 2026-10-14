use super::*;

pub(super) fn run() {
    let mut commands = Vec::new();
    let mut count = 0u32;
    let mut push = |op: u8, payload: Vec<u8>| {
        commands.push(op);
        commands.extend((payload.len() as u32).to_le_bytes());
        commands.extend(payload);
        count += 1;
    };
    for (id, kind) in [(1u32, 1u8), (2, 12), (3, 12), (4, 12)] {
        let mut p = id.to_le_bytes().to_vec();
        p.push(kind);
        push(1, p);
    }
    push(
        4,
        [1u32, 3, 2, 3, 4]
            .into_iter()
            .flat_map(u32::to_le_bytes)
            .collect(),
    );
    push(5, 1u32.to_le_bytes().to_vec());
    push(
        7,
        [640f32, 480.]
            .into_iter()
            .flat_map(f32::to_le_bytes)
            .collect(),
    );
    let mut bytes = b"DNTV".to_vec();
    bytes.extend(1u16.to_le_bytes());
    bytes.extend(16u16.to_le_bytes());
    bytes.extend(((16 + commands.len()) as u32).to_le_bytes());
    bytes.extend(count.to_le_bytes());
    bytes.extend(commands);
    let mut engine = Engine::new(Box::new(NativeMeasurer::default()));
    engine.flush(&bytes).unwrap();
    let mut registry = Registry {
        navigation: HashMap::new(),
        host: gtk::Fixed::new(),
        entries: HashMap::new(),
    };
    // The old implementation formed a cycle after its second commit. Two-radio
    // fixtures cannot expose that bug. Activation then never returned.
    for _ in 0..20 {
        registry.sync(&engine).unwrap();
        for selected in 2..=4 {
            let button = registry.entries[&selected]
                .control
                .as_ref()
                .unwrap()
                .downcast_ref::<gtk::CheckButton>()
                .unwrap();
            button.set_active(true);
            for id in 2..=4 {
                assert_eq!(
                    registry.entries[&id]
                        .control
                        .as_ref()
                        .unwrap()
                        .downcast_ref::<gtk::CheckButton>()
                        .unwrap()
                        .is_active(),
                    id == selected
                );
            }
        }
    }
    // Moving members between groups must unlink the previous native group.
    let alternate = gtk::CheckButton::new();
    registry
        .entries
        .get_mut(&3)
        .unwrap()
        .set_radio_group(&alternate);
    registry.sync(&engine).unwrap();
    registry.entries[&3]
        .control
        .as_ref()
        .unwrap()
        .downcast_ref::<gtk::CheckButton>()
        .unwrap()
        .set_active(true);
    println!("PASS GTK three-radio repeated commits, activation and regrouping");
}
