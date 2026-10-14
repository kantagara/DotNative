use gtk::{gio, prelude::*};

/// GSettings caches values and delivers changes on the GTK main context; reading
/// it here does not make synchronous D-Bus calls on the frame thread.
pub(super) struct SystemTheme {
    interface: Option<gio::Settings>,
    gtk: Option<gtk::Settings>,
}

impl SystemTheme {
    pub(super) fn new() -> Self {
        let interface = gio::SettingsSchemaSource::default()
            .and_then(|source| source.lookup("org.gnome.desktop.interface", true))
            .filter(|schema| schema.has_key("color-scheme"))
            .map(|schema| gio::Settings::new_full(&schema, None::<&gio::SettingsBackend>, None));
        Self {
            interface,
            gtk: gtk::Settings::default(),
        }
    }

    pub(super) fn current(&self) -> i32 {
        let preference = self.interface.as_ref().map(|s| s.string("color-scheme"));
        let name = self.gtk.as_ref().and_then(|s| s.gtk_theme_name());
        resolve(preference.as_deref(), name.as_deref())
    }
}

fn resolve(preference: Option<&str>, gtk_theme: Option<&str>) -> i32 {
    match preference {
        Some("prefer-dark") => 2,
        Some("prefer-light") => 1,
        _ if gtk_theme.is_some_and(|name| name.to_ascii_lowercase().contains("dark")) => 2,
        _ => 1,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn desktop_preference_overrides_legacy_theme_name() {
        assert_eq!(resolve(Some("prefer-dark"), Some("Yaru")), 2);
        assert_eq!(resolve(Some("prefer-light"), Some("Yaru-dark")), 1);
        assert_eq!(resolve(Some("default"), Some("Adwaita-dark")), 2);
        assert_eq!(resolve(None, Some("Adwaita")), 1);
        assert_eq!(resolve(None, None), 1);
    }
}
