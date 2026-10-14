//! Transfer complete compatible row trees between logical items in one commit.
//! No idle trees or item data survive an empty list. Logical IDs are never reused.
use crate::{Engine, Kind, Node};
use std::{collections::HashMap, sync::Arc};

pub(crate) fn plan(previous: &im::HashMap<u32, Arc<Node>>, engine: &Engine) -> Vec<(u32, u32)> {
    fn tree<'a>(
        id: u32,
        get: &impl Fn(u32) -> Option<&'a Node>,
        absent: &impl Fn(u32) -> bool,
        ids: &mut Vec<u32>,
        shape: &mut Vec<(u8, usize)>,
    ) -> bool {
        let Some(node) = get(id) else { return false };
        if ids.len() >= 256
            || !absent(id)
            || !matches!(node.kind, Kind::VStack | Kind::Text | Kind::Button)
            || !node.background_image.is_empty()
            || crate::events::is_interacting(id)
        {
            return false;
        }
        ids.push(id);
        shape.push((node.kind as u8, node.children.len()));
        node.children
            .iter()
            .all(|child| tree(*child, get, absent, ids, shape))
    }
    let mut result = Vec::new();
    for (list_id, list) in engine.nodes().filter(|(_, n)| n.virtual_list) {
        let Some(old_list) = previous.get(&list_id) else {
            continue;
        };
        let Some(old_document) = old_list.children.first().and_then(|id| previous.get(id)) else {
            continue;
        };
        let Some(document) = list.children.first().and_then(|id| engine.node(*id)) else {
            continue;
        };
        let mut spare: HashMap<Vec<(u8, usize)>, Vec<Vec<u32>>> = HashMap::new();
        for row in &old_document.children {
            let (mut ids, mut shape) = (Vec::new(), Vec::new());
            if tree(
                *row,
                &|id| previous.get(&id).map(AsRef::as_ref),
                &|id| engine.node(id).is_none(),
                &mut ids,
                &mut shape,
            ) {
                spare.entry(shape).or_default().push(ids);
            }
        }
        for row in &document.children {
            let (mut ids, mut shape) = (Vec::new(), Vec::new());
            if tree(
                *row,
                &|id| engine.node(id),
                &|id| !previous.contains_key(&id),
                &mut ids,
                &mut shape,
            ) {
                if let Some(old) = spare.get_mut(&shape).and_then(Vec::pop) {
                    result.extend(old.into_iter().zip(ids));
                }
            }
        }
    }
    result
}
