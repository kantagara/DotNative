//! Native containers for the managed realization window. Item identity belongs
//! to the row, never to a recycled container. Unbind before rebinding any slot.
use std::collections::{HashMap, HashSet};

pub(crate) struct CellPool<T> {
    active: HashMap<u32, T>,
    spare: Vec<T>,
}

impl<T> Default for CellPool<T> {
    fn default() -> Self {
        Self {
            active: HashMap::new(),
            spare: Vec::new(),
        }
    }
}
impl<T> CellPool<T> {
    pub(crate) fn sync(
        &mut self,
        rows: &[u32],
        mut create: impl FnMut() -> crate::Result<T>,
        mut unbind: impl FnMut(&T),
    ) -> crate::Result<()> {
        let keep: HashSet<_> = rows.iter().copied().collect();
        let retired: Vec<_> = self
            .active
            .keys()
            .filter(|id| !keep.contains(id))
            .copied()
            .collect();
        for id in retired {
            let cell = self.active.remove(&id).unwrap();
            unbind(&cell);
            self.spare.push(cell);
        }
        for id in rows {
            if !self.active.contains_key(id) {
                let cell = match self.spare.pop() {
                    Some(cell) => cell,
                    None => create()?,
                };
                self.active.insert(*id, cell);
            }
        }
        self.spare.truncate(32);
        Ok(())
    }
    pub(crate) fn get(&self, id: u32) -> &T {
        &self.active[&id]
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::Cell;
    #[test]
    fn reuses_retired_slots_and_keeps_surviving_identity() {
        let created = Cell::new(0);
        let create = || {
            created.set(created.get() + 1);
            Ok(created.get())
        };
        let mut pool = CellPool::default();
        pool.sync(&[1, 2, 3], create, |_| {}).unwrap();
        let surviving = *pool.get(2);
        let mut released = Vec::new();
        pool.sync(&[4, 2, 5], create, |cell| released.push(*cell))
            .unwrap();
        assert_eq!(*pool.get(2), surviving);
        assert_eq!(created.get(), 3);
        assert_eq!(released.len(), 2);
        assert!(released.contains(pool.get(4)) && released.contains(pool.get(5)));
        pool.sync(&[5, 4, 2], create, |_| panic!("reorder must not unbind"))
            .unwrap();
    }
    #[test]
    fn spare_pool_is_bounded_after_large_window_and_empty_data() {
        let mut pool = CellPool::default();
        pool.sync(&(1..=1000).collect::<Vec<_>>(), || Ok(()), |_| {})
            .unwrap();
        let count = Cell::new(0);
        pool.sync(&[], || unreachable!(), |_| count.set(count.get() + 1))
            .unwrap();
        assert_eq!(count.get(), 1000);
        assert!(pool.active.is_empty());
        assert_eq!(pool.spare.len(), 32);
        pool.sync(
            &(1..=32).collect::<Vec<_>>(),
            || panic!("reuse spares"),
            |_| {},
        )
        .unwrap();
    }
}
