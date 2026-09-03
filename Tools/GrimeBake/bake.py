#!/usr/bin/env python3
"""Bake weathering into the wall albedos, as seamless variants.

WHY

The NYC pack shipped its grime as HDRP decal projectors. Drawing those under URP would
mean adding the Decal Renderer Feature, a screen-space pass, and 238 live projectors per
stacked building section, in a biome already over its triangle budget. Baking the same
stains into the albedo costs nothing at render time: the wall was going to sample a
texture either way.

WHY SEAMLESS, AND WHY NOT ONE BIG STAIN

The wall albedos are authored to tile - measured edge differences of 0.007 to 0.019
between opposite sides - so the UVs repeat them across a facade. Baking one distinctive
stain into a tiling texture would show that same stain on every repeat, which reads
worse than no grime at all.

So each stain is placed with wrap-around and the placements are drawn from a fixed seed,
giving a layer that tiles as cleanly as the wall under it. What the eye gets is
weathering that varies across the surface without a repeating landmark. Variation
between buildings comes from picking a different variant per building, not from making
any single texture unique.

HOW IT COMPOSITES

Pure multiplicative darkening: out = base * (1 - coverage * (1 - stain luma)). Grime
darkens a surface and lets its texture show through, so the brick pattern survives under
the stain. Nothing is ever brightened, and a fully covered pixel still keeps the base
colour scaled by the stain's own luminance rather than replacing it.

REPRODUCING IT

The output is committed, but it is also reproducible: seeds and asset guids are derived
from the asset name, so re-running writes byte-identical files rather than churning the
repository with fresh noise.

  python3 Tools/GrimeBake/bake.py            # rewrite the textures and materials
  python3 Tools/GrimeBake/bake.py --list      # what it would produce
  python3 Tools/GrimeBake/bake.py --dry-run   # coverage and seam figures, no writes
"""
import argparse, hashlib, os, re, sys

import numpy as np
from PIL import Image

# The three wall surfaces that read as a building exterior. The rest of the pack's
# materials that reach the wall branch - rope, plastic, wood - are trim, not facade.
SLOT_BASEMAP = (r"^    - _BaseMap:\n^        m_Texture: \{[^}]*\}\n"
                r"^        m_Scale: \{(?P<s>[^}]*)\}\n^        m_Offset: \{(?P<o>[^}]*)\}\n")

WALLS = ["TexturesCom_Brick_Modern_1K_albedo",
         "TexturesCom_Plaster_Rough_1K_albedo",
         "TexturesCom_Paint_Epoxy_1K_albedo"]

# Leaves0170 is deliberately absent: it is fallen leaves, a ground decal, not wall grime.
STAINS = ["TexturesCom_DecalsLeaking0339_1_masked_S",
          "TexturesCom_DecalsLeaking0340_2_masked_S",
          "TexturesCom_DecalsStain0092_1_masked_S",
          "TexturesCom_DecalsStain0094_2_masked_S",
          "TexturesCom_DecalBottom0047_7_masked_S"]

VARIANTS = 3          # per wall, on top of the clean original
STRENGTH = [0.45, 0.65, 0.85]   # light, weathered, neglected

# Stain count and scale are tuned for coverage, not strength. The first pass used
# near-full-size patches and buried the wall under 80% grime, which reads as a filthy
# surface rather than a weathered one. Small patches leave the wall visible between the
# streaks, which is what makes it look like weather instead of paint.
STAINS_PER_VARIANT = [5, 9, 14]


def load_rgba(path):
    with open(path, "rb") as handle:
        if handle.read(40).startswith(b"version https://git-lfs"):
            raise SystemExit(
                "%s is an unfetched Git LFS pointer, not an image.\n"
                "Run: git lfs pull --include 'Tools/GrimeBake/stains/*'" % path)
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.float32) / 255.0


def luma(rgb):
    return rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def place(canvas_h, canvas_w, patch, rng):
    """Drop one stain onto a coverage canvas with wrap-around, so the result tiles.

    The patch is flipped and scaled first. np.roll after an unwrapped paste is what keeps
    a stain crossing the edge continuous rather than clipped, which is the whole reason
    the output can tile at all."""
    a = patch[..., 3]
    lum = luma(patch[..., :3])
    if rng.random() < 0.5:
        a, lum = a[:, ::-1], lum[:, ::-1]
    if rng.random() < 0.5:
        a, lum = a[::-1], lum[::-1]

    scale = rng.uniform(0.18, 0.5)
    h = max(8, min(canvas_h, int(a.shape[0] * scale)))
    w = max(8, min(canvas_w, int(a.shape[1] * scale)))
    a = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((w, h), Image.BILINEAR),
                   dtype=np.float32) / 255.0
    lum = np.asarray(Image.fromarray((lum * 255).astype(np.uint8)).resize((w, h), Image.BILINEAR),
                     dtype=np.float32) / 255.0

    cover = np.zeros((canvas_h, canvas_w), dtype=np.float32)
    dark = np.ones((canvas_h, canvas_w), dtype=np.float32)
    cover[:h, :w] = a
    dark[:h, :w] = lum
    dy, dx = rng.integers(0, canvas_h), rng.integers(0, canvas_w)
    return np.roll(cover, (dy, dx), (0, 1)), np.roll(dark, (dy, dx), (0, 1))


def grime_layer(shape, stains, seed, count):
    """Accumulated coverage and the darkening each covered pixel should apply."""
    rng = np.random.default_rng(seed)
    h, w = shape
    cover = np.zeros((h, w), dtype=np.float32)
    dark = np.ones((h, w), dtype=np.float32)
    for _ in range(count):
        patch = stains[rng.integers(0, len(stains))]
        c, d = place(h, w, patch, rng)
        # keep the darkest contribution where stains overlap, rather than stacking to black
        dark = np.where(c > cover, d, dark)
        cover = np.maximum(cover, c)
    return cover, dark


def bake(base, cover, dark, strength):
    """out = base * (1 - coverage * (1 - stain luma)); darkening only, detail preserved."""
    factor = 1.0 - np.clip(cover * strength, 0.0, 1.0) * (1.0 - dark)
    return np.clip(base * factor[..., None], 0.0, 1.0)


def seam(image):
    """Mean absolute difference between opposite edges. Near zero means it tiles."""
    return (float(np.abs(image[:, 0] - image[:, -1]).mean()),
            float(np.abs(image[0] - image[-1]).mean()))


def seed_for(name):
    """Python randomises str hashing per process, so the seed comes from sha1 instead.
    Re-running the bake has to produce the same bytes or every run churns the repo."""
    return int(hashlib.sha1(name.encode()).hexdigest()[:8], 16)


def guid_for(name):
    """A stable guid derived from the asset name, so re-running does not churn metas."""
    return hashlib.sha1(("RoadRage.GrimeBake." + name).encode()).hexdigest()[:32]


def meta_for(template, name):
    lines = open(template, encoding="utf-8").read().split("\n")
    lines[1] = "guid: " + guid_for(name)
    return "\n".join(lines)


def variant_material(clean_source, name, texture_guid):
    """A copy of the wall material with its base map swapped for the grimed one.

    Only _BaseMap moves. The normal map is deliberately shared with the clean material:
    grime is a stain on the surface, not a change in its relief, and rebinding it would
    have all four variants of a wall carry four identical copies of the same 6 MB normal
    map into the build."""
    out = re.sub(r"^  m_Name: .*$", "  m_Name: " + name, clean_source, count=1, flags=re.M)
    out = re.sub(SLOT_BASEMAP,
                 "    - _BaseMap:\n        m_Texture: {fileID: 2800000, guid: %s, type: 3}\n"
                 "        m_Scale: {\\g<s>}\n        m_Offset: {\\g<o>}\n" % texture_guid,
                 out, count=1, flags=re.M)
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--textures",
                        default="Assets/(HDRP) NYC-Like City Buildings Set (PBR)/Materials/Textures")
    parser.add_argument("--stains", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "stains"),
                        help="directory holding the stain pngs used as bake input")
    parser.add_argument("--out", default=None, help="defaults to --textures")
    parser.add_argument("--stains-per-variant", type=int, default=0,
                        help="override the per-variant stain count")
    parser.add_argument("--materials", default="Assets/Resources/Buildings/NYC/Materials",
                        help="where the wall materials live, and where variants are written")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    out_dir = args.out or args.textures

    if args.list:
        for wall in WALLS:
            for i in range(VARIANTS):
                print("  %s_grime%d  strength %.2f" % (wall, i + 1, STRENGTH[i]))
        return 0

    stains = [load_rgba(os.path.join(args.stains, n + ".png")) for n in STAINS]
    materials_written = []
    for wall in WALLS:
        src = os.path.join(args.textures, wall + ".tif")
        base = np.asarray(Image.open(src).convert("RGB"), dtype=np.float32) / 255.0
        before = seam(base)
        for i in range(VARIANTS):
            name = "%s_grime%d" % (wall, i + 1)
            cover, dark = grime_layer(base.shape[:2], stains, seed=seed_for(name),
                                      count=args.stains_per_variant or STAINS_PER_VARIANT[i])
            out = bake(base, cover, dark, STRENGTH[i])
            after = seam(out)
            print("  %-52s coverage %4.1f%%  luma %.3f -> %.3f  seam %.4f/%.4f -> %.4f/%.4f"
                  % (name, 100 * (cover > 0.1).mean(), luma(base).mean(), luma(out).mean(),
                     before[0], before[1], after[0], after[1]))
            if args.dry_run:
                continue
            path = os.path.join(out_dir, name + ".png")
            Image.fromarray((out * 255).round().astype(np.uint8)).save(path, optimize=True)
            open(path + ".meta", "w", encoding="utf-8", newline="\n").write(
                meta_for(src + ".meta", name + ".png"))

            clean_mat = os.path.join(args.materials, wall + ".mat")
            if os.path.exists(clean_mat):
                mat_path = os.path.join(args.materials, name + ".mat")
                open(mat_path, "w", encoding="utf-8", newline="").write(
                    variant_material(open(clean_mat, encoding="utf-8").read(),
                                     name, guid_for(name + ".png")))
                open(mat_path + ".meta", "w", encoding="utf-8", newline="\n").write(
                    meta_for(clean_mat + ".meta", name + ".mat"))
                materials_written.append(mat_path)
    print("GRIMEBAKE %s %d texture(s) and %d material(s)"
          % ("would write" if args.dry_run else "wrote",
             len(WALLS) * VARIANTS, len(materials_written)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
