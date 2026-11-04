mod native;
mod render;

use litematica_preview_core::{MeshStream, PreviewInfo, PreviewOptions};
use nucleation::{formats::limits::DecodeLimits, meshing::ResourcePackSource};
use serde::Deserialize;
use std::fs::File;
use std::io::{self, Read, Write};
use std::path::Path;
use std::time::{Duration, Instant};

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Request {
    input: String,
    pack: String,
    parent: isize,
    parent_process: u32,
    #[serde(default)]
    threads: Option<u8>,
}

#[derive(Deserialize)]
#[serde(tag = "command", rename_all = "camelCase", deny_unknown_fields)]
enum Command {
    Orbit {
        x: f32,
        y: f32,
    },
    Pan {
        x: f32,
        y: f32,
    },
    Zoom {
        factor: f32,
    },
    Stage {
        left: f32,
        top: f32,
        width: f32,
        height: f32,
    },
    Fit,
    Resize,
    Status,
    Close,
}

fn emit(value: serde_json::Value) {
    let mut out = io::stdout().lock();
    let _ = writeln!(out, "{value}");
    let _ = out.flush();
}

fn bounded_read(path: &str, limit: u64) -> Result<Vec<u8>, String> {
    let file = File::open(path).map_err(|e| e.to_string())?;
    if file.metadata().map_err(|e| e.to_string())?.len() > limit {
        return Err("File exceeds preview size limit".into());
    }
    let mut bytes = Vec::new();
    file.take(limit + 1)
        .read_to_end(&mut bytes)
        .map_err(|e| e.to_string())?;
    if bytes.is_empty() || bytes.len() as u64 > limit {
        return Err("Empty or oversized preview file".into());
    }
    Ok(bytes)
}

fn load(
    request: Request,
    atlas_side: u32,
    send: impl Fn(native::Work) -> Result<(), String>,
) -> Result<(), String> {
    let started = Instant::now();
    let current = || {
        if started.elapsed() < Duration::from_secs(90) {
            Ok(())
        } else {
            Err("Preview timed out".to_owned())
        }
    };
    emit(serde_json::json!({"event":"progress","phase":"decode"}));
    let data = bounded_read(&request.input, 64 * 1024 * 1024)?;
    let limits = DecodeLimits {
        max_input_bytes: 64 * 1024 * 1024,
        max_decompressed_bytes: 256 * 1024 * 1024,
        max_dimension: 4096,
        max_volume: 16 * 1024 * 1024,
        max_regions: 1024,
        max_palette_entries: 65536,
        max_entities: 100000,
        max_block_entities: 100000,
        max_nbt_depth: 64,
        max_nbt_string_bytes: 1024 * 1024,
        max_nbt_collection_items: 16 * 1024 * 1024,
        max_nbt_nodes: 32 * 1024 * 1024,
    };
    let options = PreviewOptions {
        chunk_size: Some(32),
        thread_count: request.threads,
        speed_first: request.threads.is_some(),
        ..Default::default()
    };
    let source = litematica_preview_core::decode_with_options(&data, &limits, options, current)
        .map_err(|e| e.to_string())?;
    drop(data);
    if source.block_count() == 0 {
        return Err("The schematic contains no blocks".into());
    }
    let pack_bytes = bounded_read(&request.pack, 256 * 1024 * 1024)?;
    let pack = litematica_preview_core::resource_pack_overlay(
        &ResourcePackSource::from_resource_pack(Default::default()),
        &pack_bytes,
    )?;
    drop(pack_bytes);
    let origin = source.content_origin();
    let size = source.content_dimensions();
    let min = [
        origin.0 as f32 - 0.5,
        origin.1 as f32 - 0.5,
        origin.2 as f32 - 0.5,
    ];
    send(native::Work::Prepared(
        min,
        [
            min[0] + size.0 as f32,
            min[1] + size.1 as f32,
            min[2] + size.2 as f32,
        ],
    ))?;
    let config = litematica_preview_core::mesh_config().with_atlas_max_size(atlas_side);
    let mut stream = MeshStream::new(
        &source,
        &pack,
        &config,
        usize::from(options.thread_count.unwrap_or(1)),
        &current,
    )?;
    let total = stream.batch_count();
    let mut atlas = Some(stream.take_atlas());
    let mut info = PreviewInfo {
        block_count: source.block_count(),
        block_entity_count: source.block_entity_count(),
        triangle_count: 0,
        min: [f32::INFINITY; 3],
        max: [f32::NEG_INFINITY; 3],
        texture_count: 0,
        part_count: 0,
    };
    let mut bytes = 0usize;
    let mut index = 0;
    while let Some(mut batch) = stream.next(&current)? {
        if let Some(texture) = atlas.take() {
            batch.textures.insert(0, texture);
        }
        let batch_bytes =
            batch.geometry.len() + batch.textures.iter().map(|t| t.pixels.len()).sum::<usize>();
        bytes = bytes
            .checked_add(batch_bytes)
            .ok_or("Geometry size overflow")?;
        if bytes > 512 * 1024 * 1024 {
            return Err("Geometry exceeds 512 MiB preview limit".into());
        }
        info.triangle_count += batch.info.triangle_count;
        info.part_count += batch.info.part_count;
        info.texture_count = batch.info.texture_count;
        if !batch.parts.is_empty() {
            for axis in 0..3 {
                info.min[axis] = info.min[axis].min(batch.info.min[axis]);
                info.max[axis] = info.max[axis].max(batch.info.max[axis]);
            }
        }
        send(native::Work::Batch(batch))?;
        index += 1;
        emit(
            serde_json::json!({"event":"progress","phase":"mesh","completed":index,"total":total}),
        );
    }
    if info.part_count == 0 {
        return Err("The schematic contains no visible geometry".into());
    }
    send(native::Work::Complete(info))?;
    Ok(())
}

fn run() -> Result<(), String> {
    let args: Vec<_> = std::env::args_os().collect();
    if args.len() != 2 {
        return Err("Expected a request JSON file".into());
    }
    let request: Request =
        serde_json::from_slice(&bounded_read(&args[1].to_string_lossy(), 32768)?)
            .map_err(|e| e.to_string())?;
    if Path::new(&request.input)
        .extension()
        .and_then(|s| s.to_str())
        .map(|s| s.eq_ignore_ascii_case("litematic"))
        != Some(true)
    {
        return Err("Expected a .litematic file".into());
    }
    native::run(request)
}

fn main() {
    if let Err(error) = run() {
        emit(serde_json::json!({"event":"error","message":error}));
        std::process::exit(1);
    }
}
