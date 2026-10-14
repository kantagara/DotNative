//! Bounded process-local registration. Registration owns resources independently of a node.
use crate::{Error, Node, Result};
use std::{any::Any, cell::RefCell, collections::HashMap, sync::Arc};
#[derive(Clone, Debug, PartialEq)]
pub struct FontAsset {
    pub family: Arc<str>,
    pub face: Arc<str>,
    pub weight: u16,
    pub italic: bool,
    pub data: Arc<[u8]>,
}
impl FontAsset {
    pub fn new(family: &str, data: &[u8]) -> Result<Self> {
        if family.is_empty()
            || family.contains('\0')
            || data.len() < 12
            || data.len() > 8 * 1024 * 1024
        {
            return Err(Error::Wire("invalid font asset"));
        }
        if &data[..4] != b"OTTO" && data[..4] != [0, 1, 0, 0] {
            return Err(Error::Wire("font asset must be a single-face TTF/OTF"));
        }
        let u16_at = |p: usize| -> Result<usize> {
            Ok(u16::from_be_bytes(
                data.get(p..p + 2)
                    .ok_or(Error::Wire("truncated font"))?
                    .try_into()
                    .unwrap(),
            ) as usize)
        };
        let u32_at = |p: usize| -> Result<usize> {
            Ok(u32::from_be_bytes(
                data.get(p..p + 4)
                    .ok_or(Error::Wire("truncated font"))?
                    .try_into()
                    .unwrap(),
            ) as usize)
        };
        let count = u16_at(4)?;
        if count > 256 {
            return Err(Error::Wire("too many font tables"));
        }
        let mut names = None;
        let mut style = None;
        for i in 0..count {
            let entry = 12 + i * 16;
            let tag = data
                .get(entry..entry + 4)
                .ok_or(Error::Wire("truncated font table"))?;
            let start = u32_at(entry + 8)?;
            let len = u32_at(entry + 12)?;
            let end = start
                .checked_add(len)
                .ok_or(Error::Wire("font table overflow"))?;
            data.get(start..end)
                .ok_or(Error::Wire("invalid font table range"))?;
            if tag == b"OS/2" {
                if len < 64 {
                    return Err(Error::Wire("truncated OS/2 font style"));
                }
                let weight = u16_at(start + 4)? as u16;
                if !(1..=1000).contains(&weight) {
                    return Err(Error::Wire("invalid font weight"));
                }
                style = Some((weight, u16_at(start + 62)? & 1 != 0));
            }
            if tag == b"name" {
                names = Some((start, end));
            }
        }
        let (start, end) = names.ok_or(Error::Wire("font has no name table"))?;
        if end - start < 6 {
            return Err(Error::Wire("truncated font names"));
        }
        let count = u16_at(start + 2)?;
        let storage = start + u16_at(start + 4)?;
        if count > 4096 || start + 6 + count * 12 > end || storage > end {
            return Err(Error::Wire("invalid font name records"));
        }
        let mut matches = false;
        let mut face: Option<String> = None;
        for i in 0..count {
            let p = start + 6 + i * 12;
            let platform = u16_at(p)?;
            let name_id = u16_at(p + 6)?;
            let len = u16_at(p + 8)?;
            let offset = storage + u16_at(p + 10)?;
            if offset + len > end {
                return Err(Error::Wire("invalid font name range"));
            }
            if (name_id == 1 || name_id == 16 || name_id == 6)
                && (platform == 0 || platform == 3)
                && len % 2 == 0
            {
                let units: Vec<u16> = data[offset..offset + len]
                    .chunks_exact(2)
                    .map(|v| u16::from_be_bytes([v[0], v[1]]))
                    .collect();
                if let Ok(name) = String::from_utf16(&units) {
                    if name_id == 6 {
                        if name.is_empty()
                            || name.len() > 63
                            || !name
                                .bytes()
                                .all(|c| (33..=126).contains(&c) && !b"[](){}<>/%".contains(&c))
                            || face.as_ref().is_some_and(|old| old != &name)
                        {
                            return Err(Error::Wire(
                                "invalid or inconsistent PostScript font name",
                            ));
                        }
                        face = Some(name);
                    } else {
                        matches |= name.eq_ignore_ascii_case(family);
                    }
                }
            }
        }
        if !matches {
            return Err(Error::Wire(
                "font family does not match embedded Unicode family name",
            ));
        }
        let (weight, italic) = style.ok_or(Error::Wire("font has no OS/2 style"))?;
        Ok(Self {
            weight,
            italic,
            family: Arc::from(family),
            face: Arc::from(face.ok_or(Error::Wire("font has no Unicode PostScript name"))?),
            data: Arc::from(data),
        })
    }
}
#[cfg_attr(target_os = "windows", allow(dead_code))]
struct Registered {
    asset: FontAsset,
    _resource: Box<dyn Any>,
}
thread_local! {static FONTS:RefCell<HashMap<String,Registered>>=RefCell::new(HashMap::new());}
#[cfg_attr(target_os = "windows", allow(dead_code))]
pub fn ensure(
    node: &Node,
    register: impl FnOnce(&FontAsset) -> Result<Box<dyn Any>>,
) -> Result<()> {
    let Some(asset) = &node.font_asset else {
        return Ok(());
    };
    FONTS.with(|fonts| {
        let mut fonts = fonts.borrow_mut();
        let key = asset.face.to_string();
        if let Some(old) = fonts.get(&key) {
            if !old.asset.family.eq_ignore_ascii_case(&asset.family) {
                return Err(Error::Measurement(
                    "font face already registered under another family name",
                ));
            }
            return if Arc::ptr_eq(&old.asset.data, &asset.data) || old.asset.data == asset.data {
                Ok(())
            } else {
                Err(Error::Measurement(
                    "different font data already registered for this face",
                ))
            };
        }
        if fonts.values().any(|old| {
            old.asset.family.eq_ignore_ascii_case(&asset.family)
                && old.asset.weight == asset.weight
                && old.asset.italic == asset.italic
        }) {
            return Err(Error::Measurement(
                "font family already contains this weight and slant",
            ));
        }
        if fonts.len() >= 64
            || fonts.values().map(|f| f.asset.data.len()).sum::<usize>() + asset.data.len()
                > 32 * 1024 * 1024
        {
            return Err(Error::Measurement("font registration budget exceeded"));
        }
        let resource = register(asset)?;
        fonts.insert(
            key,
            Registered {
                asset: asset.clone(),
                _resource: resource,
            },
        );
        Ok(())
    })
}
