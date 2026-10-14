//! Plugin-owned native views placed in a layout slot.
//!
//! `create` returns one owning reference. The host releases it after `detach`.
//! `detach` and `rebind` may be null. Registration and invocation stay on the
//! runner thread that owns the engine. An unknown type leaves the slot empty.

use std::{cell::RefCell, collections::HashMap, ffi::c_void, ptr::NonNull};

type Create = unsafe extern "C" fn(u32, *mut c_void) -> *mut c_void;
type Detach = unsafe extern "C" fn(u32, *mut c_void, *mut c_void);
type Rebind = unsafe extern "C" fn(u32, u32, *mut c_void, *mut c_void);

#[derive(Clone, Copy)]
struct Factory {
    create: Create,
    detach: Option<Detach>,
    rebind: Option<Rebind>,
    context: *mut c_void,
}

thread_local! {
    static REGISTRY: RefCell<HashMap<String, Factory>> = RefCell::new(HashMap::new());
}

pub(crate) fn valid_type(name: &str) -> bool {
    (1..=64).contains(&name.len())
        && name
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || matches!(byte, b'.' | b'_' | b'-'))
}

fn factory(type_name: &str) -> Option<Factory> {
    REGISTRY.with(|registry| registry.borrow().get(type_name).copied())
}

pub(crate) fn create(node_id: u32, type_name: &str) -> Option<NonNull<c_void>> {
    let factory = factory(type_name)?;
    NonNull::new(unsafe { (factory.create)(node_id, factory.context) })
}

pub(crate) fn detach(node_id: u32, type_name: &str, view: *const c_void) {
    if view.is_null() {
        return;
    }
    if let Some(detach) = factory(type_name).and_then(|factory| factory.detach) {
        let context = factory(type_name).unwrap().context;
        unsafe { detach(node_id, view.cast_mut(), context) };
    }
}

pub(crate) fn rebind(old_id: u32, new_id: u32, type_name: &str, view: *const c_void) {
    if old_id == new_id || view.is_null() {
        return;
    }
    if let Some(rebind) = factory(type_name).and_then(|factory| factory.rebind) {
        let context = factory(type_name).unwrap().context;
        unsafe { rebind(old_id, new_id, view.cast_mut(), context) };
    }
}

/// # Safety
/// `name` points at `name_len` readable bytes. Callbacks, when non-null, stay
/// valid until process exit and are invoked only on this thread.
#[no_mangle]
pub unsafe extern "C" fn dotnative_register_platform_view(
    name: *const u8,
    name_len: usize,
    create: Option<Create>,
    detach: Option<Detach>,
    rebind: Option<Rebind>,
    context: *mut c_void,
) -> i32 {
    if crate::bind_thread().is_err() || name.is_null() || create.is_none() || name_len > 64 {
        return -1;
    }
    let bytes = unsafe { std::slice::from_raw_parts(name, name_len) };
    let Ok(name) = std::str::from_utf8(bytes) else {
        return -1;
    };
    if !valid_type(name) {
        return -1;
    }
    REGISTRY.with(|registry| {
        let mut registry = registry.borrow_mut();
        if registry.len() >= 64 && !registry.contains_key(name) {
            return -1;
        }
        registry.insert(
            name.to_string(),
            Factory {
                create: create.unwrap(),
                detach,
                rebind,
                context,
            },
        );
        0
    })
}
