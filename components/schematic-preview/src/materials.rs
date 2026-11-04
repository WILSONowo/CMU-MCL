use nucleation::meshing::ResourcePackSource;
use serde::{Deserialize, Serialize};
use std::path::PathBuf;

pub const BASE_PACK: &[u8] = include_bytes!("../resources/base-pack.zip");

// PackLayer and compose_resource_packs are reused from LitematicaPreview
// v0.5.2 src-tauri/src/preview.rs, with the upstream ordering unchanged.
/// One resource pack in priority order, highest first. `name` identifies the
/// pack in errors.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PackLayer {
    pub name: String,
    pub path: PathBuf,
}

/// Overlays `layers` from lowest to highest on one clone of the parsed base.
/// The bundled vanilla pack stays the final per-key fallback.
pub fn compose_resource_packs(
    base: &ResourcePackSource,
    layers: &[PackLayer],
    mut read: impl FnMut(&PackLayer) -> Result<Vec<u8>, String>,
) -> Result<ResourcePackSource, String> {
    let empty = ResourcePackSource::from_resource_pack(Default::default());
    let mut merged = base.pack().clone();
    for layer in layers.iter().rev() {
        let bytes = read(layer).map_err(|error| {
            format!(
                "The texture pack “{}” could not be read: {error}",
                layer.name
            )
        })?;
        let mut parsed =
            litematica_preview_core::resource_pack_overlay(&empty, &bytes).map_err(|error| {
                format!(
                    "The texture pack “{}” could not be used: {error}",
                    layer.name
                )
            })?;
        merged.overlay(std::mem::take(parsed.pack_mut()));
    }
    Ok(ResourcePackSource::from_resource_pack(merged))
}

pub fn load(legacy: &str, layers: &[PackLayer]) -> Result<ResourcePackSource, String> {
    if layers.len() > 16 {
        return Err("Choose at most 16 custom resource packs".into());
    }
    if !legacy.is_empty() && !layers.is_empty() {
        return Err("Choose one resource-pack request format".into());
    }
    let base = ResourcePackSource::from_bytes(BASE_PACK)
        .map_err(|error| format!("The bundled block resources are invalid: {error}"))?;
    let legacy_layers;
    let layers = if legacy.is_empty() {
        layers
    } else {
        legacy_layers = [PackLayer {
            name: "Selected resource pack".into(),
            path: legacy.into(),
        }];
        &legacy_layers
    };
    let mut total = 0usize;
    compose_resource_packs(&base, layers, |layer| {
        let path = layer.path.to_str().ok_or("Invalid resource pack path")?;
        let bytes = super::bounded_read(path, 256 * 1024 * 1024)?;
        total = total
            .checked_add(bytes.len())
            .ok_or("Resource pack size overflow")?;
        if total > 256 * 1024 * 1024 {
            return Err("Custom resource packs exceed 256 MiB in total".into());
        }
        Ok(bytes)
    })
}

#[cfg(test)]
#[path = "materials_tests.rs"]
mod tests;
