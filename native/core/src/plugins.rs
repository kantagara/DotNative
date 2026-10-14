//! Session-scoped plugin requests. Native callbacks borrow request bytes only
//! during dispatch; replies are copied into a bounded, thread-safe queue.
use std::{
    collections::{HashMap, VecDeque},
    sync::{Mutex, OnceLock},
};
const LIMIT: usize = 1024 * 1024;
type Handler = unsafe extern "C" fn(*const u8, usize);
#[derive(Default)]
struct State {
    handlers: HashMap<u32, Handler>,
    pending: HashMap<u64, u32>,
    replies: VecDeque<Vec<u8>>,
    bytes: usize,
}
fn state() -> &'static Mutex<State> {
    static STATE: OnceLock<Mutex<State>> = OnceLock::new();
    STATE.get_or_init(|| Mutex::new(State::default()))
}

#[cfg(target_os = "android")]
pub(crate) fn detach() {
    dotnative_plugins_reset();
    state().lock().unwrap().handlers.clear();
}

#[no_mangle]
pub extern "C" fn dotnative_register_channel(id: u32, callback: Option<Handler>) -> i32 {
    if crate::bind_thread().is_err() || id == 0 {
        return -1;
    }
    let Some(callback) = callback else {
        return -1;
    };
    let mut s = state().lock().unwrap();
    if s.handlers.contains_key(&id) || s.handlers.len() >= 256 {
        return -1;
    }
    s.handlers.insert(id, callback);
    0
}

pub(crate) fn send(bytes: &[u8]) -> i32 {
    if crate::bind_thread().is_err() || bytes.len() < 16 || bytes.len() > LIMIT + 16 {
        return -1;
    }
    let id = u64::from_le_bytes(bytes[..8].try_into().unwrap());
    let channel = u32::from_le_bytes(bytes[8..12].try_into().unwrap());
    let kind = u32::from_le_bytes(bytes[12..16].try_into().unwrap());
    if id == 0 || !matches!(kind, 1 | 2) || (kind == 2 && bytes.len() != 16) {
        return -1;
    }
    let callback = {
        let mut s = state().lock().unwrap();
        if kind == 1 {
            if s.pending.contains_key(&id) || s.pending.len() + s.replies.len() >= 256 {
                return -1;
            }
            s.pending.insert(id, channel);
        } else {
            if s.pending.get(&id) != Some(&channel) {
                return 0;
            }
            s.pending.remove(&id);
        }
        s.handlers.get(&channel).copied()
    };
    if let Some(callback) = callback {
        unsafe { callback(bytes.as_ptr(), bytes.len()) };
    } else if kind == 1 {
        reply(&failure(
            id,
            "missing_plugin",
            "Plugin channel is not registered on this platform.",
        ));
    }
    0
}
fn string(out: &mut Vec<u8>, value: &str) {
    out.push(4);
    out.extend_from_slice(&(value.len() as u32).to_le_bytes());
    out.extend_from_slice(value.as_bytes());
}
fn failure(id: u64, code: &str, message: &str) -> Vec<u8> {
    let mut out = id.to_le_bytes().to_vec();
    out.extend_from_slice(&1u32.to_le_bytes());
    out.push(6);
    out.extend_from_slice(&2u32.to_le_bytes());
    string(&mut out, "code");
    string(&mut out, code);
    string(&mut out, "message");
    string(&mut out, message);
    out
}
fn reply(bytes: &[u8]) -> i32 {
    if bytes.len() < 13 || bytes.len() > LIMIT + 12 {
        return -1;
    }
    let id = u64::from_le_bytes(bytes[..8].try_into().unwrap());
    let status = u32::from_le_bytes(bytes[8..12].try_into().unwrap());
    if status > 2 {
        return -1;
    }
    let mut s = state().lock().unwrap();
    if !s.pending.contains_key(&id) {
        return 1;
    }
    // Reserve space for error replies so pressure never leaves a caller waiting forever.
    let overloaded = s.bytes + bytes.len() > 15 * LIMIT;
    let owned = if overloaded {
        failure(id, "busy", "Plugin reply queue is full.")
    } else {
        bytes.to_vec()
    };
    s.pending.remove(&id);
    s.bytes += owned.len();
    s.replies.push_back(owned);
    if overloaded {
        2
    } else {
        0
    }
}

/// End the session on the UI thread. Registration survives, resources do not.
#[no_mangle]
pub extern "C" fn dotnative_plugins_reset() -> i32 {
    if crate::bind_thread().is_err() {
        return -1;
    }
    let callbacks = {
        let mut s = state().lock().unwrap();
        s.pending.clear();
        s.replies.clear();
        s.bytes = 0;
        s.handlers
            .iter()
            .map(|(&id, &handler)| (id, handler))
            .collect::<Vec<_>>()
    };
    for (id, handler) in callbacks {
        let mut bytes = [0u8; 16];
        bytes[8..12].copy_from_slice(&id.to_le_bytes());
        bytes[12..].copy_from_slice(&3u32.to_le_bytes());
        unsafe { handler(bytes.as_ptr(), bytes.len()) };
    }
    0
}
/// # Safety
/// `bytes` must point to `len` readable bytes for this call. No pointer is retained.
#[no_mangle]
pub unsafe extern "C" fn dotnative_send_plugin(bytes: *const u8, len: usize) -> i32 {
    if bytes.is_null() || !(16..=LIMIT + 16).contains(&len) {
        return -1;
    }
    send(unsafe { std::slice::from_raw_parts(bytes, len) })
}
/// # Safety
/// `bytes` must point to `len` readable bytes. May be called off the UI thread.
#[no_mangle]
pub unsafe extern "C" fn dotnative_plugin_reply(bytes: *const u8, len: usize) -> i32 {
    if bytes.is_null() || !(13..=LIMIT + 12).contains(&len) {
        return -1;
    }
    reply(unsafe { std::slice::from_raw_parts(bytes, len) })
}
/// # Safety
/// Non-null `buffer` points to `capacity` writable bytes. Insufficient capacity
/// returns required length without consuming a reply.
#[no_mangle]
pub unsafe extern "C" fn dotnative_poll_plugin(buffer: *mut u8, capacity: usize) -> usize {
    let mut s = state().lock().unwrap();
    let Some(front) = s.replies.front() else {
        return 0;
    };
    let size = front.len();
    if capacity < size || buffer.is_null() {
        return size;
    }
    unsafe {
        std::ptr::copy_nonoverlapping(front.as_ptr(), buffer, size);
    }
    s.replies.pop_front();
    s.bytes -= size;
    size
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn replies_are_copied_bounded_and_polled_without_consuming_on_probe() {
        {
            let mut state = state().lock().unwrap();
            *state = State::default();
            state.pending.insert(7, 42);
        }
        let mut bytes = 7u64.to_le_bytes().to_vec();
        bytes.extend_from_slice(&0u32.to_le_bytes());
        bytes.push(0);
        assert_eq!(reply(&bytes), 0);
        bytes[12] = 2; // Caller-owned memory is never retained.
        assert_eq!(reply(&bytes), 1); // Exactly one terminal reply.
        assert_eq!(
            unsafe { dotnative_poll_plugin(std::ptr::null_mut(), 0) },
            13
        );
        let mut small = [0u8; 2];
        assert_eq!(
            unsafe { dotnative_poll_plugin(small.as_mut_ptr(), small.len()) },
            13
        );
        let mut output = [0u8; 13];
        assert_eq!(
            unsafe { dotnative_poll_plugin(output.as_mut_ptr(), output.len()) },
            13
        );
        assert_eq!(output[12], 0);
        assert_eq!(unsafe { dotnative_poll_plugin(std::ptr::null_mut(), 0) }, 0);
        assert_eq!(reply(&[0; 12]), -1);
        assert_eq!(unsafe { dotnative_send_plugin(std::ptr::null(), 16) }, -1);
        {
            let mut state = state().lock().unwrap();
            state.pending.insert(8, 42);
            state.bytes = 15 * LIMIT;
        }
        bytes[..8].copy_from_slice(&8u64.to_le_bytes());
        assert_eq!(reply(&bytes), 2);
        let mut state = state().lock().unwrap();
        let failure = state.replies.front().unwrap();
        assert_eq!(u32::from_le_bytes(failure[8..12].try_into().unwrap()), 1);
        assert!(state.bytes < 16 * LIMIT);
        *state = State::default();
    }
}
