//! Experimental desktop presentation: shared protocol, separate native toolkits.
//! This first slice deliberately rejects unsupported controls before committing
//! a batch. It never silently substitutes a button for another control type.
#[cfg(target_os = "linux")]
mod linux;
#[cfg(target_os = "windows")]
mod windows;

use crate::Result;
#[cfg(target_os = "linux")]
use crate::{Engine, Frame, Kind};

#[cfg(target_os = "linux")]
pub(crate) use linux::flush;
#[cfg(target_os = "windows")]
pub(crate) use windows::flush;
#[cfg(target_os = "windows")]
pub(crate) use windows::wake_ui;

#[cfg(all(feature = "dev-host", target_os = "linux"))]
pub use linux::run_preview;
#[cfg(all(feature = "dev-host", target_os = "windows"))]
pub use windows::run_preview;

fn validate(bytes: &[u8]) -> Result<()> {
    crate::protocol::parse(bytes)?;
    Ok(())
}

#[cfg(target_os = "linux")]
fn hierarchy(engine: &Engine) -> Result<Vec<(u32, u32, Frame)>> {
    let mut output = Vec::with_capacity(engine.node_count());
    let mut pending = if engine.root() == 0 {
        vec![]
    } else {
        vec![(engine.root(), 0)]
    };
    while let Some((id, parent)) = pending.pop() {
        output.push((id, parent, engine.frame(id)?));
        for child in engine.node(id).unwrap().children.iter().rev() {
            pending.push((*child, id));
        }
    }
    Ok(output)
}

#[cfg(feature = "dev-host")]
fn require(status: i32) {
    if status < 0 {
        eprintln!("DotNative desktop lifecycle failed ({status})");
        std::process::exit(1);
    }
}

/// Real native smoke test used on target OS CI runners. Creates a tree through
/// the production wire protocol; platform tests click the actual native button.
#[cfg(feature = "dev-host")]
fn smoke_commands() -> Vec<u8> {
    let mut commands = Vec::new();
    let mut count = 0u32;
    let mut push = |opcode: u8, payload: &[u8]| {
        commands.push(opcode);
        commands.extend((payload.len() as u32).to_le_bytes());
        commands.extend(payload);
        count += 1;
    };
    for (id, kind) in [(1u32, 1u8), (2, 2), (3, 3)] {
        let mut payload = id.to_le_bytes().to_vec();
        payload.push(kind);
        push(1, &payload);
    }
    for (id, text) in [(2u32, "Desktop smoke"), (3, "Increment")] {
        let mut payload = id.to_le_bytes().to_vec();
        // Text payload includes its UTF-8 byte length.
        payload.extend((text.len() as u32).to_le_bytes());
        payload.extend(text.as_bytes());
        push(2, &payload);
    }
    let mut children = 1u32.to_le_bytes().to_vec();
    children.extend(2u32.to_le_bytes());
    children.extend(2u32.to_le_bytes());
    children.extend(3u32.to_le_bytes());
    push(4, &children);
    push(5, &1u32.to_le_bytes());
    let mut viewport = 640f32.to_le_bytes().to_vec();
    viewport.extend(480f32.to_le_bytes());
    push(7, &viewport);
    let mut bytes = b"DNTV".to_vec();
    bytes.extend(1u16.to_le_bytes());
    bytes.extend(16u16.to_le_bytes());
    bytes.extend(((16 + commands.len()) as u32).to_le_bytes());
    bytes.extend(count.to_le_bytes());
    bytes.extend(commands);
    bytes
}

#[cfg(feature = "dev-host")]
fn smoke_commands_update() -> Vec<u8> {
    let mut commands = Vec::new();
    let mut push = |opcode: u8, payload: &[u8]| {
        commands.push(opcode);
        commands.extend((payload.len() as u32).to_le_bytes());
        commands.extend(payload);
    };
    let text = b"Count: 1";
    let mut payload = 2u32.to_le_bytes().to_vec();
    payload.extend((text.len() as u32).to_le_bytes());
    payload.extend(text);
    push(2, &payload);
    let mut children = 1u32.to_le_bytes().to_vec();
    children.extend(1u32.to_le_bytes());
    children.extend(2u32.to_le_bytes());
    push(4, &children);
    push(6, &3u32.to_le_bytes());
    let mut bytes = b"DNTV".to_vec();
    bytes.extend(1u16.to_le_bytes());
    bytes.extend(16u16.to_le_bytes());
    bytes.extend(((16 + commands.len()) as u32).to_le_bytes());
    bytes.extend(3u32.to_le_bytes());
    bytes.extend(commands);
    bytes
}

#[cfg(all(feature = "dev-host", target_os = "linux"))]
fn verify_unsupported_batch_is_atomic(flush: fn(&[u8]) -> Result<()>) {
    let mut unsupported = smoke_commands();
    // Invalid kind must be rejected before changing the live tree.
    unsupported[25] = 255;
    assert!(flush(&unsupported).is_err());
    crate::ENGINE.with(|engine| {
        let engine = engine.borrow();
        let engine = engine.as_ref().unwrap();
        assert_eq!(engine.node_count(), 3);
        assert_eq!(engine.node(1).unwrap().kind, Kind::VStack);
    });
}

#[cfg(all(feature = "dev-host", target_os = "linux"))]
fn smoke_styles(styled: bool) -> Vec<u8> {
    let mut commands = Vec::new();
    let mut push = |opcode: u8, payload: &[u8]| {
        commands.push(opcode);
        commands.extend((payload.len() as u32).to_le_bytes());
        commands.extend(payload);
    };
    let mut visual = 2u32.to_le_bytes().to_vec();
    visual.extend((if styled { 0.6f32 } else { 1. }).to_le_bytes());
    visual.extend((if styled { 2f32 } else { 0. }).to_le_bytes());
    visual.extend([0, 0, 255, 255]);
    visual.extend([0; 4]);
    for value in [0f32, 0., 0., 0., 0., 1., 1., 0.] {
        visual.extend(value.to_le_bytes());
    }
    visual.push(1);
    push(17, &visual);
    let mut text = 2u32.to_le_bytes().to_vec();
    text.extend((if styled { 700u16 } else { 400 }).to_le_bytes());
    text.extend([styled as u8, styled as u8]);
    text.extend((if styled { 2i32 } else { -1 }).to_le_bytes());
    text.push(if styled { 5 } else { 0 });
    text.extend((if styled { 1f32 } else { 0. }).to_le_bytes());
    text.extend((if styled { 24f32 } else { 0. }).to_le_bytes());
    text.push(if styled { 3 } else { 0 });
    text.extend(0u32.to_le_bytes());
    push(18, &text);
    let mut gradient = 1u32.to_le_bytes().to_vec();
    gradient.extend(if styled { [1, 2] } else { [0, 0] });
    for value in [0f32, 0., 1., 0.] {
        gradient.extend(value.to_le_bytes());
    }
    if styled {
        gradient.extend(0f32.to_le_bytes());
        gradient.extend([255, 0, 0, 255]);
        gradient.extend(1f32.to_le_bytes());
        gradient.extend([0, 0, 255, 255]);
    }
    push(21, &gradient);
    let mut decoration = 2u32.to_le_bytes().to_vec();
    decoration.push(if styled { 3 } else { 0 });
    for value in [20f32, 0., 4., 0.] {
        decoration.extend(value.to_le_bytes());
    }
    for _ in 0..4 {
        decoration.extend(4f32.to_le_bytes());
        decoration.extend([255, 0, 0, 255, 2]);
    }
    push(26, &decoration);
    push(27, &[3, 0, 0, 0, if styled { 0 } else { 1 }]);
    let mut z = 2u32.to_le_bytes().to_vec();
    z.extend((if styled { 1i32 } else { 0 }).to_le_bytes());
    push(22, &z);
    let mut result = b"DNTV\x01\x00\x10\x00".to_vec();
    result.extend(((commands.len() + 16) as u32).to_le_bytes());
    result.extend(6u32.to_le_bytes());
    result.extend(commands);
    result
}

#[cfg(all(feature = "dev-host", target_os = "linux"))]
fn smoke_image(create: bool, fit: u8, tint: bool) -> Vec<u8> {
    let mut commands = Vec::new();
    let mut count = 0u32;
    let mut push = |opcode: u8, payload: &[u8]| {
        commands.push(opcode);
        commands.extend((payload.len() as u32).to_le_bytes());
        commands.extend(payload);
        count += 1;
    };
    if create {
        push(1, &[4, 0, 0, 0, 8]);
        let image = include_bytes!("../../../testdata/image-fit.png");
        let mut p = 4u32.to_le_bytes().to_vec();
        p.extend((image.len() as u32).to_le_bytes());
        p.extend(image);
        push(10, &p);
        let mut children = 1u32.to_le_bytes().to_vec();
        for v in [2u32, 2, 4] {
            children.extend(v.to_le_bytes());
        }
        push(4, &children);
    }
    push(23, &[4, 0, 0, 0, fit]);
    push(24, &[4, 0, 0, 0, tint as u8, 0, 255, 0, 128]);
    let data: &[u8] = if tint {
        include_bytes!("../../../testdata/image-fit.png")
    } else {
        &[]
    };
    let mut bg = 1u32.to_le_bytes().to_vec();
    bg.push(0);
    bg.extend((data.len() as u32).to_le_bytes());
    bg.extend(data);
    push(25, &bg);
    let mut result = b"DNTV\x01\x00\x10\x00".to_vec();
    result.extend(((commands.len() + 16) as u32).to_le_bytes());
    result.extend(count.to_le_bytes());
    result.extend(commands);
    result
}

#[cfg(all(feature = "dev-host", target_os = "linux"))]
fn smoke_rich(styled: bool) -> Vec<u8> {
    let mut p = 2u32.to_le_bytes().to_vec();
    p.extend((if styled { 1u32 } else { 0 }).to_le_bytes());
    if styled {
        let text = "Desktop smoke";
        p.extend((text.len() as u32).to_le_bytes());
        p.extend(text.as_bytes());
        p.push(1);
        p.extend(48f32.to_le_bytes());
        p.extend([255, 0, 0, 255]);
        p.extend(400u16.to_le_bytes());
        p.extend([0, 0]);
        p.extend(0u32.to_le_bytes());
    }
    let mut b = b"DNTV\x01\x00\x10\x00".to_vec();
    b.extend(((21 + p.len()) as u32).to_le_bytes());
    b.extend(1u32.to_le_bytes());
    b.push(28);
    b.extend((p.len() as u32).to_le_bytes());
    b.extend(p);
    b
}

#[cfg(target_os = "linux")]
mod font_assets_linux;

#[cfg(all(feature = "dev-host", target_os = "linux"))]
fn expect_state(id: u32, kind: u32, value: f32) {
    let mut event = [0u8; 256];
    let mut found = false;
    while unsafe { crate::events::dotnative_poll_event(event.as_mut_ptr(), event.len()) } != 0 {
        found |= u32::from_le_bytes(event[..4].try_into().unwrap()) == id
            && u32::from_le_bytes(event[4..8].try_into().unwrap()) == kind
            && f32::from_le_bytes(event[8..12].try_into().unwrap()) == value;
    }
    assert!(found, "native state {kind} event for {id}: {value}");
}

/// Statically linked SDK plugins register after the UI thread and native window exist.
#[cfg(feature = "dev-host")]
fn register_plugins(window: *mut std::ffi::c_void) {
    #[cfg(dotnative_native_plugins)]
    unsafe {
        extern "C" {
            fn dotnative_register_plugins(window: *mut std::ffi::c_void) -> i32;
        }
        require(dotnative_register_plugins(window));
    }
    #[cfg(not(dotnative_native_plugins))]
    let _ = window;
}
