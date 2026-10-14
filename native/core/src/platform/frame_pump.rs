//! Bounded compositor notifications; the wait runs off the UI thread.
use std::{
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc,
    },
    thread::{self, JoinHandle},
    time::{Duration, Instant},
};
struct State {
    stopped: AtomicBool,
    pending: AtomicBool,
}
pub(crate) struct FramePump {
    state: Arc<State>,
    worker: Option<JoinHandle<()>>,
}
impl FramePump {
    /// `wait` must return within 100 ms. False means timeout/occlusion/failure.
    pub(crate) fn start(
        mut wait: impl FnMut() -> bool + Send + 'static,
        mut post: impl FnMut() -> bool + Send + 'static,
    ) -> std::io::Result<Self> {
        let state = Arc::new(State {
            stopped: AtomicBool::new(false),
            pending: AtomicBool::new(false),
        });
        let worker_state = state.clone();
        let worker = thread::Builder::new()
            .name("dotnative-compositor".into())
            .spawn(move || {
                while !worker_state.stopped.load(Ordering::Acquire) {
                    let started = Instant::now();
                    let frame = wait();
                    if worker_state.stopped.load(Ordering::Acquire) {
                        break;
                    }
                    if frame {
                        if !worker_state.pending.swap(true, Ordering::AcqRel) && !post() {
                            break;
                        }
                    } else {
                        // Some APIs return immediately while the display is off.
                        thread::sleep(Duration::from_millis(100).saturating_sub(started.elapsed()));
                    }
                }
                worker_state.stopped.store(true, Ordering::Release);
            })?;
        Ok(Self {
            state,
            worker: Some(worker),
        })
    }
    pub(crate) fn acknowledge(&self) {
        self.state.pending.store(false, Ordering::Release);
    }
    pub(crate) fn stop(&mut self) {
        self.state.stopped.store(true, Ordering::Release);
        if let Some(worker) = self.worker.take() {
            let _ = worker.join();
        }
    }
}
impl Drop for FramePump {
    fn drop(&mut self) {
        self.stop();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::mpsc;
    #[test]
    fn coalesces_until_ui_ack_and_stops_notifications() {
        let (send, receive) = mpsc::channel();
        let mut pump = FramePump::start(
            || {
                thread::sleep(Duration::from_millis(2));
                true
            },
            move || send.send(()).is_ok(),
        )
        .unwrap();
        receive.recv_timeout(Duration::from_secs(1)).unwrap();
        assert!(receive.recv_timeout(Duration::from_millis(30)).is_err());
        pump.acknowledge();
        receive.recv_timeout(Duration::from_secs(1)).unwrap();
        pump.stop();
        assert!(receive.recv_timeout(Duration::from_millis(30)).is_err());
    }
    #[test]
    fn occlusion_and_errors_back_off_instead_of_spinning() {
        let (send, receive) = mpsc::channel();
        let mut pump = FramePump::start(
            move || {
                send.send(Instant::now()).unwrap();
                false
            },
            || panic!("failed clock posted a frame"),
        )
        .unwrap();
        let first = receive.recv_timeout(Duration::from_secs(2)).unwrap();
        let second = receive.recv_timeout(Duration::from_secs(2)).unwrap();
        pump.stop();
        assert!(second.duration_since(first) >= Duration::from_millis(90));
    }
}
