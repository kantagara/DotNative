//! Development-only loopback transport. Networking stays off the OS UI thread;
//! the UI thread alone consumes commands, computes layout and polls native input.
use std::{
    cell::{Cell, RefCell},
    collections::VecDeque,
    io::{self, Read, Write},
    net::SocketAddr,
    sync::mpsc::{self, Receiver, SyncSender, TrySendError},
};
#[cfg(not(all(target_os = "ios", not(target_abi = "sim"))))]
use std::{net::TcpStream, time::Duration};
fn trace_handshake(message: &str) {
    if std::env::var_os("DOTNATIVE_DEVHOST_TRACE").is_some() {
        eprintln!("DotNative handshake: {message}");
    }
}
const LIMIT: usize = 16 * 1024 * 1024;
const PLATFORM: u32 = if cfg!(target_os = "ios") {
    1
} else if cfg!(target_os = "android") {
    2
} else if cfg!(target_os = "windows") {
    4
} else if cfg!(target_os = "linux") {
    5
} else {
    3
};
const PORT: u16 = 4970 + PLATFORM as u16;
fn preview_port(configured: Option<&str>) -> Result<u16, &'static str> {
    match configured {
        None => Ok(PORT),
        Some(value) => value
            .trim()
            .parse::<u16>()
            .ok()
            .filter(|port| *port != 0)
            .ok_or("DOTNATIVE_PREVIEW_PORT must be between 1 and 65535"),
    }
}
struct Session {
    outgoing: SyncSender<Vec<u8>>,
    incoming: Receiver<(u32, Vec<u8>)>,
    pending: VecDeque<Vec<u8>>,
    pending_bytes: usize,
    frame_pending: bool,
}
thread_local! {
    static SESSION: RefCell<Option<Session>> = const { RefCell::new(None) };
    static ACCESSIBILITY: Cell<(i32,f32,i32)> = const { Cell::new((0,1.,0)) };
    static THEME: Cell<i32> = const { Cell::new(1) };
}
fn packet(kind: u32, body: &[u8]) -> Vec<u8> {
    let mut bytes = Vec::with_capacity(8 + body.len());
    bytes.extend(kind.to_le_bytes());
    bytes.extend((body.len() as u32).to_le_bytes());
    bytes.extend(body);
    bytes
}
fn queue(kind: u32, body: &[u8]) {
    SESSION.with(|slot| {
        if let Some(session) = slot.borrow_mut().as_mut() {
            let bytes = packet(kind, body);
            // A stalled host must not grow the native process without a bound.
            if session.pending_bytes + bytes.len() > LIMIT {
                eprintln!("DotNative dev host is not consuming input; reconnect the preview.");
                return;
            }
            session.pending_bytes += bytes.len();
            session.pending.push_back(bytes);
        }
    });
}
fn valid_envelope(kind: u32, length: usize) -> bool {
    (matches!(kind, 100 | 101) && (16..=LIMIT).contains(&length)) || (kind == 102 && length == 0)
}
fn acknowledge_frame() {
    SESSION.with(|slot| {
        if let Some(session) = slot.borrow_mut().as_mut() {
            session.frame_pending = false;
        }
    });
}
fn queue_frame() {
    SESSION.with(|slot| {
        if let Some(session) = slot.borrow_mut().as_mut() {
            if !session.frame_pending && session.pending_bytes <= LIMIT - 8 {
                session.frame_pending = true;
                session.pending.push_back(packet(9, &[]));
                session.pending_bytes += 8;
            }
        }
    });
}
fn network(outgoing: Receiver<Vec<u8>>, incoming: SyncSender<(u32, Vec<u8>)>) {
    let configured = std::env::var("DOTNATIVE_PREVIEW_PORT").ok();
    let port = match preview_port(configured.as_deref()) {
        Ok(port) => port,
        Err(error) => {
            eprintln!("{error}");
            return;
        }
    };
    #[cfg(all(target_os = "ios", not(target_abi = "sim")))]
    let mut stream = match std::net::TcpListener::bind(SocketAddr::from(([127, 0, 0, 1], port)))
        .and_then(|listener| listener.accept())
    {
        Ok((stream, _)) => stream,
        Err(error) => {
            eprintln!("DotNative USB listener failed: {error}");
            return;
        }
    };
    #[cfg(not(all(target_os = "ios", not(target_abi = "sim"))))]
    let mut stream = {
        let address = SocketAddr::from(([127, 0, 0, 1], port));
        let mut attempts = 0;
        let stream = loop {
            match TcpStream::connect_timeout(&address, Duration::from_millis(500)) {
                Ok(stream) => break stream,
                Err(error) => {
                    attempts += 1;
                    trace_handshake(&format!("connect attempt {attempts} failed: {error}"));
                    if attempts >= 120 {
                        eprintln!("DotNative dev host did not start on {address}");
                        return;
                    }
                    std::thread::sleep(Duration::from_millis(250));
                }
            }
        };
        stream
    };
    let result = (|| -> io::Result<()> {
        trace_handshake("connected");
        stream.set_nodelay(true)?;
        let mut writer = stream.try_clone()?;
        std::thread::spawn(move || {
            trace_handshake("writer started");
            while let Ok(bytes) = outgoing.recv() {
                if bytes.starts_with(&1u32.to_le_bytes()) {
                    trace_handshake("writer received hello");
                }
                if writer.write_all(&bytes).is_err() {
                    break;
                }
            }
            let _ = writer.shutdown(std::net::Shutdown::Both);
        });
        loop {
            let mut header = [0u8; 8];
            stream.read_exact(&mut header)?;
            let kind = u32::from_le_bytes(header[..4].try_into().unwrap());
            let length = u32::from_le_bytes(header[4..].try_into().unwrap()) as usize;
            if !valid_envelope(kind, length) {
                return Err(io::Error::new(
                    io::ErrorKind::InvalidData,
                    "invalid preview command envelope",
                ));
            }
            let mut bytes = vec![0; length];
            stream.read_exact(&mut bytes)?;
            if incoming.send((kind, bytes)).is_err() {
                return Ok(());
            }
            #[cfg(all(target_os = "windows", feature = "desktop"))]
            if kind != 102 {
                crate::platform::desktop::wake_ui();
            }
        }
    })();
    eprintln!("DotNative preview disconnected ({result:?}); keeping the last frame. Restart the native preview after a host restart.");
}
#[no_mangle]
pub extern "C" fn dotnative_app_start(width: f32, height: f32) -> i32 {
    if !width.is_finite() || !height.is_finite() || width <= 0. || height <= 0. {
        return -1;
    }
    let (tx, rx) = mpsc::sync_channel(64);
    let (commands_tx, commands_rx) = mpsc::sync_channel(8);
    SESSION.with(|slot| {
        *slot.borrow_mut() = Some(Session {
            outgoing: tx,
            incoming: commands_rx,
            pending: VecDeque::new(),
            pending_bytes: 0,
            frame_pending: false,
        })
    });
    std::thread::spawn(move || network(rx, commands_tx));
    let mut hello = 1u32.to_le_bytes().to_vec();
    hello.extend(PLATFORM.to_le_bytes());
    hello.extend(width.to_le_bytes());
    hello.extend(height.to_le_bytes());
    THEME.with(|theme| hello.extend(theme.get().to_le_bytes()));
    ACCESSIBILITY.with(|p| {
        let (motion, scale, contrast) = p.get();
        hello.extend(motion.to_le_bytes());
        hello.extend(scale.to_le_bytes());
        hello.extend(contrast.to_le_bytes());
    });
    queue(1, &hello);
    trace_handshake("hello queued on UI thread");
    0
}
#[no_mangle]
pub extern "C" fn dotnative_app_insets(top: f32, bottom: f32, keyboard: f32) -> i32 {
    if !top.is_finite()
        || !bottom.is_finite()
        || !keyboard.is_finite()
        || top < 0.
        || bottom < 0.
        || keyboard < 0.
    {
        return -1;
    }
    0
}
#[no_mangle]
pub extern "C" fn dotnative_app_resize(width: f32, height: f32) -> i32 {
    if !width.is_finite() || !height.is_finite() || width < 0. || height < 0. {
        return -1;
    }
    let mut body = width.to_le_bytes().to_vec();
    body.extend(height.to_le_bytes());
    queue(2, &body);
    0
}
#[no_mangle]
pub extern "C" fn dotnative_app_accessibility(motion: i32, scale: f32, contrast: i32) -> i32 {
    if !(0..=1).contains(&motion)
        || !(0..=1).contains(&contrast)
        || !scale.is_finite()
        || !(0.5..=5.).contains(&scale)
    {
        return -1;
    }
    let changed = ACCESSIBILITY.with(|p| {
        let changed = p.get() != (motion, scale, contrast);
        p.set((motion, scale, contrast));
        changed
    });
    if changed {
        let mut bytes = motion.to_le_bytes().to_vec();
        bytes.extend(scale.to_le_bytes());
        bytes.extend(contrast.to_le_bytes());
        queue(8, &bytes);
    }
    0
}
#[no_mangle]
pub extern "C" fn dotnative_app_system_theme(theme: i32) -> i32 {
    if theme != 1 && theme != 2 {
        return -1;
    }
    THEME.with(|value| value.set(theme));
    queue(4, &theme.to_le_bytes());
    0
}
#[no_mangle]
pub extern "C" fn dotnative_app_activity(activity: i32) -> i32 {
    if !(0..=2).contains(&activity) {
        return -1;
    }
    queue(5, &activity.to_le_bytes());
    0
}
#[no_mangle]
pub extern "C" fn dotnative_app_stop() -> i32 {
    crate::plugins::dotnative_plugins_reset();
    SESSION.with(|slot| slot.borrow_mut().take());
    0
}
#[no_mangle]
pub extern "C" fn dotnative_app_tick() -> i32 {
    tick(true)
}
pub(crate) fn tick(emit_frame: bool) -> i32 {
    // Release the session borrow before executing UIKit: native callbacks can
    // enqueue lifecycle changes while applying a committed tree.
    for _ in 0..8 {
        let bytes = SESSION.with(|slot| {
            slot.borrow()
                .as_ref()
                .and_then(|session| match session.incoming.try_recv() {
                    Ok(packet) => Some(packet),
                    Err(mpsc::TryRecvError::Empty) => None,
                    Err(mpsc::TryRecvError::Disconnected) => Some((0, Vec::new())),
                })
        });
        let Some((kind, bytes)) = bytes else {
            break;
        };
        if kind == 0 {
            SESSION.with(|slot| slot.borrow_mut().take());
            crate::plugins::dotnative_plugins_reset();
            break;
        }
        if kind == 102 {
            acknowledge_frame();
            continue;
        }
        if kind == 101 {
            if crate::plugins::send(&bytes) != 0 {
                return -1;
            }
            continue;
        }
        unsafe {
            crate::flush_commands(bytes.as_ptr(), bytes.len());
        }
        // Sent only after parsing, layout and native presentation have succeeded.
        queue(6, &[]);
    }
    for _ in 0..32 {
        let room = SESSION.with(|slot| {
            slot.borrow()
                .as_ref()
                .is_some_and(|s| s.pending_bytes < LIMIT - 1024 * 1024 - 20)
        });
        if !room {
            break;
        }
        let required = unsafe { crate::plugins::dotnative_poll_plugin(std::ptr::null_mut(), 0) };
        if required == 0 {
            break;
        }
        let mut reply = vec![0u8; required];
        let size =
            unsafe { crate::plugins::dotnative_poll_plugin(reply.as_mut_ptr(), reply.len()) };
        if size == 0 {
            break;
        }
        queue(7, &reply[..size]);
    }
    for _ in 0..32 {
        let room =
            SESSION.with(|slot| slot.borrow().as_ref().is_some_and(|s| s.pending.len() < 64));
        if !room {
            break;
        }
        let mut event = vec![0u8; 4096];
        let mut count =
            unsafe { crate::events::dotnative_poll_event(event.as_mut_ptr(), event.len()) };
        if count == 0 {
            break;
        }
        if count > event.len() {
            if count > LIMIT {
                return -1;
            }
            event.resize(count, 0);
            count = unsafe { crate::events::dotnative_poll_event(event.as_mut_ptr(), event.len()) };
        }
        queue(3, &event[..count]);
    }
    if emit_frame {
        queue_frame();
    }
    SESSION.with(|slot| {
        if let Some(session) = slot.borrow_mut().as_mut() {
            while let Some(bytes) = session.pending.pop_front() {
                let length = bytes.len();
                if bytes.starts_with(&1u32.to_le_bytes()) {
                    trace_handshake("UI tick sending hello");
                }
                match session.outgoing.try_send(bytes) {
                    Ok(()) => session.pending_bytes -= length,
                    Err(TrySendError::Full(bytes)) => {
                        session.pending.push_front(bytes);
                        break;
                    }
                    Err(TrySendError::Disconnected(_)) => {
                        session.pending.clear();
                        session.pending_bytes = 0;
                        break;
                    }
                }
            }
        }
    });
    0
}

#[cfg(test)]
mod tests {
    #[test]
    fn isolated_preview_port_rejects_invalid_endpoints() {
        assert_eq!(super::preview_port(None), Ok(super::PORT));
        assert_eq!(super::preview_port(Some("14974")), Ok(14974));
        for value in ["0", "65536", "-1", "", "localhost:4974"] {
            assert!(super::preview_port(Some(value)).is_err());
        }
    }
    use super::*;
    #[test]
    fn frame_pulses_are_bounded_until_acknowledged() {
        let (outgoing, _reader) = mpsc::sync_channel(1);
        let (_writer, incoming) = mpsc::sync_channel(1);
        SESSION.with(|slot| {
            *slot.borrow_mut() = Some(Session {
                outgoing,
                incoming,
                pending: VecDeque::new(),
                pending_bytes: 0,
                frame_pending: false,
            })
        });
        for _ in 0..100 {
            queue_frame();
        }
        SESSION.with(|slot| {
            let mut slot = slot.borrow_mut();
            let session = slot.as_mut().unwrap();
            assert_eq!(session.pending.len(), 1);
            assert_eq!(session.pending_bytes, 8);
            assert_eq!(session.pending.pop_front().unwrap(), packet(9, &[]));
            session.pending_bytes = 0;
        });
        acknowledge_frame();
        queue_frame();
        SESSION.with(|slot| assert_eq!(slot.borrow_mut().take().unwrap().pending.len(), 1));
        assert!(valid_envelope(102, 0));
        assert!(!valid_envelope(102, 16));
        assert!(!valid_envelope(100, 0));
        assert!(valid_envelope(100, 16));
    }
}
