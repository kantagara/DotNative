//! UI-thread event handoff. No managed callback can reenter a Rust layout commit.
use std::{
    cell::RefCell,
    collections::{HashMap, VecDeque},
};
struct Event {
    id: u32,
    kind: u32,
    value: f32,
    text: String,
}
thread_local! {static EVENTS:RefCell<VecDeque<Event>>=const{RefCell::new(VecDeque::new())};}
// Native callbacks may keep a physical control ID while its logical item changes.
// Resolve at enqueue time: already queued events keep their old logical ID.
#[derive(Default)]
struct Bindings {
    physical: HashMap<u32, u32>,
    logical: HashMap<u32, u32>,
    interacting: HashMap<u32, u8>,
}
thread_local! { static BINDINGS: RefCell<Bindings> = RefCell::new(Bindings::default()); }
#[allow(dead_code)]
pub(crate) fn rebind(old: u32, new: u32) {
    BINDINGS.with(|b| {
        let mut b = b.borrow_mut();
        let physical = b.logical.remove(&old).unwrap_or(old);
        b.physical.insert(physical, new);
        b.logical.insert(new, physical);
        b.interacting.remove(&old);
    });
}
#[allow(dead_code)]
pub(crate) fn is_interacting(id: u32) -> bool {
    BINDINGS.with(|b| b.borrow().interacting.get(&id).copied().unwrap_or(0) != 0)
}
#[allow(dead_code)]
pub(crate) fn retain_bindings(live: impl Fn(u32) -> bool) {
    BINDINGS.with(|b| {
        let mut b = b.borrow_mut();
        b.physical.retain(|_, id| live(*id));
        b.logical.retain(|id, _| live(*id));
        b.interacting.retain(|id, _| live(*id));
    });
}
#[cfg_attr(target_os = "windows", allow(dead_code))]
pub(crate) fn enqueue(id: u32) {
    emit(id, 1, 0., String::new());
}
pub(crate) fn emit(id: u32, kind: u32, value: f32, text: String) {
    if id == 0 && !matches!(kind, 20 | 21) {
        return;
    }
    if text.len() > 1024 * 1024 || !value.is_finite() {
        std::process::abort();
    }
    let id = BINDINGS.with(|b| {
        let mut b = b.borrow_mut();
        let id = b.physical.get(&id).copied().unwrap_or(id);
        let bit = match kind {
            4 => 1,
            7 => 2,
            8 => 4,
            _ => 0,
        };
        if bit != 0 {
            let state = b.interacting.entry(id).or_default();
            if value != 0. {
                *state |= bit;
            } else {
                *state &= !bit;
            }
            if *state == 0 {
                b.interacting.remove(&id);
            }
        }
        id
    });
    EVENTS.with(|q| {
        let mut q = q.borrow_mut();
        if matches!(kind, 20 | 21) && q.iter().filter(|e| matches!(e.kind, 20 | 21)).count() >= 32 {
            if let Some(index) = q.iter().position(|e| matches!(e.kind, 20 | 21)) {
                q.remove(index);
            }
        }
        if matches!(kind, 10 | 11) {
            if let Some(index) = q.iter().position(|e| e.id == id && e.kind == kind) {
                q.remove(index);
            }
        }
        if matches!(kind, 7 | 8)
            && q.back()
                .is_some_and(|e| e.id == id && e.kind == kind && e.value == value)
        {
            return;
        }
        if q.len() >= 1024 {
            std::process::abort();
        }
        q.push_back(Event {
            id,
            kind,
            value,
            text,
        });
    });
}
#[cfg(target_os = "android")]
pub(crate) fn clear() {
    EVENTS.with(|q| q.borrow_mut().clear());
    BINDINGS.with(|b| *b.borrow_mut() = Bindings::default());
}
/// # Safety
/// buffer points to capacity writable bytes, exclusively borrowed for this call.
/// Returns required bytes without consuming if capacity is insufficient; 0 is empty.
#[no_mangle]
pub unsafe extern "C" fn dotnative_poll_event(buffer: *mut u8, capacity: usize) -> usize {
    if crate::bind_thread().is_err() {
        std::process::abort();
    }
    EVENTS.with(|q| {
        let mut q = q.borrow_mut();
        let Some(event) = q.front() else {
            return 0;
        };
        let length = 16 + event.text.len();
        if capacity < length {
            return length;
        }
        if buffer.is_null() {
            std::process::abort();
        }
        let out = unsafe { std::slice::from_raw_parts_mut(buffer, length) };
        out[..4].copy_from_slice(&event.id.to_le_bytes());
        out[4..8].copy_from_slice(&event.kind.to_le_bytes());
        out[8..12].copy_from_slice(&event.value.to_le_bytes());
        out[12..16].copy_from_slice(&(event.text.len() as u32).to_le_bytes());
        out[16..].copy_from_slice(event.text.as_bytes());
        q.pop_front();
        length
    })
}
// Compatibility for the original counter runner. New runners poll typed events.
#[no_mangle]
pub extern "C" fn dotnative_poll_click() -> u32 {
    if crate::bind_thread().is_err() {
        std::process::abort();
    }
    EVENTS.with(|q| {
        let mut q = q.borrow_mut();
        while let Some(e) = q.pop_front() {
            if e.kind == 1 {
                return e.id;
            }
        }
        0
    })
}
#[cfg(test)]
pub(crate) fn take_for_test() -> Option<(u32, u32, f32, String)> {
    EVENTS.with(|q| {
        q.borrow_mut()
            .pop_front()
            .map(|e| (e.id, e.kind, e.value, e.text))
    })
}
#[cfg(test)]
mod tests {
    #[test]
    fn activation_export_retains_launch_and_rejects_invalid_input() {
        super::EVENTS.with(|q| q.borrow_mut().clear());
        let link = "https://example.test/game/1".as_bytes();
        unsafe {
            super::dotnative_open_activation(20, link.as_ptr(), link.len());
            super::dotnative_open_activation(1, link.as_ptr(), link.len());
            super::dotnative_open_activation(21, std::ptr::null(), 1);
            super::dotnative_open_activation(21, link.as_ptr(), 16385);
        }
        assert_eq!(
            super::take_for_test(),
            Some((0, 20, 0., String::from_utf8(link.to_vec()).unwrap()))
        );
        assert!(super::take_for_test().is_none());
    }

    #[test]
    fn scroll_events_coalesce_without_reordering_other_input() {
        super::EVENTS.with(|q| q.borrow_mut().clear());
        super::emit(4, 10, 1., "0:400".into());
        super::emit(5, 1, 0., String::new());
        for i in 0..10000 {
            super::emit(4, 10, i as f32, "0:400".into());
        }
        super::EVENTS.with(|q| {
            let mut q = q.borrow_mut();
            assert_eq!(q.len(), 2);
            assert_eq!(q.pop_front().unwrap().id, 5);
            assert_eq!(q.pop_front().unwrap().value, 9999.);
        });
    }
    #[test]
    fn typed_events_preserve_order_and_text() {
        super::enqueue(3);
        super::emit(7, 2, 0., "Živeli 🙂".into());
        super::EVENTS.with(|q| {
            let mut q = q.borrow_mut();
            assert_eq!(q.pop_front().unwrap().id, 3);
            assert_eq!(q.pop_front().unwrap().text, "Živeli 🙂");
            assert!(q.is_empty());
        });
    }
}

#[cfg(test)]
mod recycling_tests {
    #[test]
    fn rebind_preserves_queued_identity_and_releases_aliases() {
        use super::*;
        EVENTS.with(|q| q.borrow_mut().clear());
        BINDINGS.with(|b| *b.borrow_mut() = Bindings::default());
        emit(101, 1, 0., String::new());
        rebind(101, 201);
        emit(101, 1, 0., String::new());
        rebind(201, 301);
        emit(101, 1, 0., String::new());
        assert_eq!(take_for_test().unwrap().0, 101);
        assert_eq!(take_for_test().unwrap().0, 201);
        assert_eq!(take_for_test().unwrap().0, 301);
        emit(101, 7, 1., String::new());
        assert!(is_interacting(301));
        emit(101, 7, 0., String::new());
        assert!(!is_interacting(301));
        retain_bindings(|_| false);
        BINDINGS.with(|b| {
            let b = b.borrow();
            assert!(b.physical.is_empty() && b.logical.is_empty() && b.interacting.is_empty());
        });
        EVENTS.with(|q| q.borrow_mut().clear());
    }
}

/// OS activation handoff; native runners call on the UI thread.
/// # Safety
/// Called on the UI thread. text points to length UTF-8 bytes for the duration of this call.
#[no_mangle]
pub unsafe extern "C" fn dotnative_open_activation(kind: u32, text: *const u8, length: usize) {
    if !matches!(kind, 20 | 21) || text.is_null() || length > 16384 {
        return;
    }
    if let Ok(value) = std::str::from_utf8(unsafe { std::slice::from_raw_parts(text, length) }) {
        emit(0, kind, 0., value.to_owned());
    }
}
