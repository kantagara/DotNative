fn main() {
    println!("cargo:rustc-check-cfg=cfg(dotnative_native_plugins)");
    println!("cargo:rerun-if-env-changed=DOTNATIVE_PLUGIN_ARCHIVE");
    if let Some(archive) = std::env::var_os("DOTNATIVE_PLUGIN_ARCHIVE").filter(|v| !v.is_empty()) {
        let path = std::path::Path::new(&archive);
        assert!(
            path.is_absolute() && path.is_file(),
            "DOTNATIVE_PLUGIN_ARCHIVE must name a built absolute archive"
        );
        println!("cargo:rerun-if-changed={}", path.display());
        println!(
            "cargo:rustc-link-search=native={}",
            path.parent().unwrap().display()
        );
        println!("cargo:rustc-link-lib=static=dotnative_plugins");
        if std::env::var("CARGO_CFG_TARGET_OS").unwrap() == "linux" {
            println!("cargo:rustc-link-lib=dylib=stdc++");
        }
        println!("cargo:rustc-cfg=dotnative_native_plugins");
    }
    // Common Controls v6 is required for native button measurement and themed
    // controls. The crate handles MSVC and GNU resource embedding without an
    // external resource compiler, including cross-compilation.
    if std::env::var_os("CARGO_CFG_WINDOWS").is_some()
        && std::env::var_os("CARGO_FEATURE_DESKTOP").is_some()
        && std::env::var_os("CARGO_FEATURE_DEV_HOST").is_some()
    {
        embed_manifest::embed_manifest(embed_manifest::new_manifest("DotNative.DesktopPreview"))
            .expect("embed desktop application manifest");
        // embed-manifest emits link-arg-bins only. libtest also creates native
        // child windows and needs the same OS compatibility/Controls v6 context.
        let out = std::path::PathBuf::from(std::env::var_os("OUT_DIR").unwrap());
        if std::env::var("CARGO_CFG_TARGET_ENV").unwrap() == "msvc" {
            println!("cargo:rustc-link-arg=/MANIFEST:EMBED");
            println!(
                "cargo:rustc-link-arg=/MANIFESTINPUT:{}",
                out.join("manifest.xml").display()
            );
            println!("cargo:rustc-link-arg=/MANIFESTUAC:NO");
        }
    }
    println!("cargo:rerun-if-changed=build.rs");
}
