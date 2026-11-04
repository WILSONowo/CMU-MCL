use super::*;

#[test]
fn bundled_base_contains_complete_vanilla_models_states_and_textures() {
    let base = load("", &[]).unwrap();
    assert!(base.pack().blockstate_count() > 1000);
    assert!(base.pack().model_count() > 2000);
    assert!(base.pack().texture_count() > 1000);
    assert!(base.pack().get_blockstate("minecraft:stone").is_some());
    assert!(base.pack().get_model("minecraft:block/stone").is_some());
    assert!(base.get_texture_pixels("minecraft:block/stone").is_some());
}

#[test]
fn upstream_priority_composes_high_then_low_then_base() {
    let high = resource_pack(&[("shared", [0, 0, 255, 255])]);
    let low = resource_pack(&[("shared", [255, 0, 0, 255]), ("low_only", [0, 255, 0, 255])]);
    let base = ResourcePackSource::from_bytes(&resource_pack(&[("base_only", [10, 20, 30, 255])]))
        .unwrap();
    let layers = [
        PackLayer {
            name: "High".into(),
            path: "high.zip".into(),
        },
        PackLayer {
            name: "Low".into(),
            path: "low.zip".into(),
        },
    ];
    let composed = compose_resource_packs(&base, &layers, |layer| {
        Ok(if layer.name == "High" {
            high.clone()
        } else {
            low.clone()
        })
    })
    .unwrap();
    assert_eq!(
        composed.get_texture_pixels("minecraft:block/shared"),
        Some([0, 0, 255, 255].as_slice())
    );
    assert_eq!(
        composed.get_texture_pixels("minecraft:block/low_only"),
        Some([0, 255, 0, 255].as_slice())
    );
    assert_eq!(
        composed.get_texture_pixels("minecraft:block/base_only"),
        Some([10, 20, 30, 255].as_slice())
    );
    assert_eq!(base.get_texture_pixels("minecraft:block/shared"), None);
    let reversed =
        compose_resource_packs(&base, &[layers[1].clone(), layers[0].clone()], |layer| {
            Ok(if layer.name == "High" {
                high.clone()
            } else {
                low.clone()
            })
        })
        .unwrap();
    assert_eq!(
        reversed.get_texture_pixels("minecraft:block/shared"),
        Some([255, 0, 0, 255].as_slice())
    );
}

#[test]
fn partial_texture_pack_keeps_vanilla_models_and_blockstates() {
    let base = ResourcePackSource::from_bytes(BASE_PACK).unwrap();
    let layer = PackLayer {
        name: "Texture only".into(),
        path: "partial.zip".into(),
    };
    let result = compose_resource_packs(&base, &[layer], |_| {
        Ok(resource_pack(&[("stone", [255, 0, 0, 255])]))
    })
    .unwrap();
    assert_eq!(
        result.get_texture_pixels("minecraft:block/stone"),
        Some([255, 0, 0, 255].as_slice())
    );
    assert!(result.pack().get_model("minecraft:block/stone").is_some());
    assert!(result.pack().get_blockstate("minecraft:stone").is_some());
    assert_eq!(
        result.get_texture_pixels("minecraft:block/oak_planks"),
        base.get_texture_pixels("minecraft:block/oak_planks")
    );
}

#[test]
fn invalid_custom_pack_is_reported_with_its_name() {
    let base = ResourcePackSource::from_resource_pack(Default::default());
    let layer = PackLayer {
        name: "Broken".into(),
        path: "broken.zip".into(),
    };
    let error = compose_resource_packs(&base, &[layer], |_| Ok(b"not a zip".to_vec()))
        .err()
        .unwrap();
    assert!(error.contains("Broken"));
}

#[test]
fn too_many_packs_and_ambiguous_legacy_requests_are_rejected() {
    let layer = PackLayer {
        name: "test".into(),
        path: "test.zip".into(),
    };
    assert!(load("", &vec![layer.clone(); 17]).is_err());
    assert!(load("legacy.zip", &[layer]).is_err());
}

// ZIP/PNG test fixture helpers copied from the upstream preview.rs tests.
fn resource_pack(textures: &[(&str, [u8; 4])]) -> Vec<u8> {
    let mut files = vec![(
        "pack.mcmeta".to_owned(),
        br#"{"pack":{"pack_format":15,"description":"test"}}"#.to_vec(),
    )];
    for (name, color) in textures {
        files.push((
            format!("assets/minecraft/textures/block/{name}.png"),
            texture_png(*color),
        ));
    }
    zip_bytes(&files)
}

fn texture_png(color: [u8; 4]) -> Vec<u8> {
    let raw = [0, color[0], color[1], color[2], color[3]];
    let mut zlib = vec![0x78, 0x01, 0x01, 0x05, 0x00, 0xfa, 0xff];
    zlib.extend_from_slice(&raw);
    zlib.extend_from_slice(&adler32(&raw).to_be_bytes());
    let mut png = vec![0x89, b'P', b'N', b'G', 0x0d, 0x0a, 0x1a, 0x0a];
    png_chunk(&mut png, b"IHDR", &[0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
    png_chunk(&mut png, b"IDAT", &zlib);
    png_chunk(&mut png, b"IEND", &[]);
    png
}

fn png_chunk(png: &mut Vec<u8>, kind: &[u8; 4], data: &[u8]) {
    png.extend_from_slice(&(data.len() as u32).to_be_bytes());
    png.extend_from_slice(kind);
    png.extend_from_slice(data);
    let mut crc = Vec::with_capacity(4 + data.len());
    crc.extend_from_slice(kind);
    crc.extend_from_slice(data);
    png.extend_from_slice(&crc32(&crc).to_be_bytes());
}

fn adler32(data: &[u8]) -> u32 {
    let mut a = 1_u32;
    let mut b = 0_u32;
    for byte in data {
        a = (a + u32::from(*byte)) % 65_521;
        b = (b + a) % 65_521;
    }
    (b << 16) | a
}

fn zip_bytes(entries: &[(String, Vec<u8>)]) -> Vec<u8> {
    let mut archive = Vec::new();
    let mut directory = Vec::new();
    for (name, data) in entries {
        let offset = archive.len() as u32;
        let crc = crc32(data);
        archive.extend_from_slice(&0x0403_4b50_u32.to_le_bytes());
        archive.extend_from_slice(&20_u16.to_le_bytes());
        archive.extend_from_slice(&[0; 2]);
        archive.extend_from_slice(&[0; 2]);
        archive.extend_from_slice(&[0; 4]);
        archive.extend_from_slice(&crc.to_le_bytes());
        archive.extend_from_slice(&(data.len() as u32).to_le_bytes());
        archive.extend_from_slice(&(data.len() as u32).to_le_bytes());
        archive.extend_from_slice(&(name.len() as u16).to_le_bytes());
        archive.extend_from_slice(&0_u16.to_le_bytes());
        archive.extend_from_slice(name.as_bytes());
        archive.extend_from_slice(data);

        directory.extend_from_slice(&0x0201_4b50_u32.to_le_bytes());
        directory.extend_from_slice(&20_u16.to_le_bytes());
        directory.extend_from_slice(&20_u16.to_le_bytes());
        directory.extend_from_slice(&[0; 2]);
        directory.extend_from_slice(&[0; 2]);
        directory.extend_from_slice(&[0; 4]);
        directory.extend_from_slice(&crc.to_le_bytes());
        directory.extend_from_slice(&(data.len() as u32).to_le_bytes());
        directory.extend_from_slice(&(data.len() as u32).to_le_bytes());
        directory.extend_from_slice(&(name.len() as u16).to_le_bytes());
        directory.extend_from_slice(&[0; 2]);
        directory.extend_from_slice(&[0; 2]);
        directory.extend_from_slice(&[0; 2]);
        directory.extend_from_slice(&[0; 2]);
        directory.extend_from_slice(&[0; 4]);
        directory.extend_from_slice(&offset.to_le_bytes());
        directory.extend_from_slice(name.as_bytes());
    }
    let directory_offset = archive.len() as u32;
    archive.extend_from_slice(&directory);
    archive.extend_from_slice(&0x0605_4b50_u32.to_le_bytes());
    archive.extend_from_slice(&[0; 4]);
    archive.extend_from_slice(&(entries.len() as u16).to_le_bytes());
    archive.extend_from_slice(&(entries.len() as u16).to_le_bytes());
    archive.extend_from_slice(&(directory.len() as u32).to_le_bytes());
    archive.extend_from_slice(&directory_offset.to_le_bytes());
    archive.extend_from_slice(&[0; 2]);
    archive
}

fn crc32(data: &[u8]) -> u32 {
    let mut crc = !0_u32;
    for byte in data {
        crc ^= u32::from(*byte);
        for _ in 0..8 {
            crc = (crc >> 1) ^ (0xedb8_8320 & (crc & 1).wrapping_neg());
        }
    }
    !crc
}
