#!/usr/bin/env python3
"""Assertions for bake.py, run against synthetic images.

Three properties matter and none of them are visible in a thumbnail. The output has to
stay as tileable as the wall it came from, or the grime shows a repeating landmark on
every facade. It has to darken and never brighten, or "grime" turns into bleach. And it
has to be reproducible, or every run rewrites the textures with different noise.

  python3 Tools/GrimeBake/test_bake.py
"""
import importlib.util, os, sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("bake", os.path.join(HERE, "bake.py"))
bake = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bake)

failures = []


def check(label, condition, detail=""):
    if not condition:
        failures.append(label)
    print(("  ok   " if condition else "  FAIL ") + label + ("" if condition else "  " + detail))


def tiling_base(size=128):
    """A seamless base: built from sines whose period divides the size exactly."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    a = 0.5 + 0.25 * np.sin(2 * np.pi * 4 * x / size) * np.cos(2 * np.pi * 4 * y / size)
    return np.repeat(a[..., None], 3, axis=2)


def stain(h=40, w=30, alpha=0.8, lum=0.3):
    s = np.zeros((h, w, 4), dtype=np.float32)
    s[..., :3] = lum
    s[..., 3] = alpha
    return s


def main():
    size = 128
    base = tiling_base(size)
    stains = [stain(), stain(50, 20, 0.6, 0.2)]

    before = bake.seam(base)

    cover, dark = bake.grime_layer((size, size), stains, seed=7, count=10)
    check("grime layer: something was placed", cover.max() > 0.1)
    check("grime layer: not everything is covered", (cover > 0.1).mean() < 0.95)

    out = bake.bake(base, cover, dark, 0.6)
    after = bake.seam(out)
    # Opposite edges of a tiling texture are adjacent, not identical, so a seam is only
    # a seam if the join stands out against the image's own neighbour-to-neighbour
    # variation. Comparing against that makes the check scale-free rather than tied to
    # how smooth this particular fixture happens to be.
    internal = max(float(np.abs(np.diff(out, axis=1)).mean()),
                   float(np.abs(np.diff(out, axis=0)).mean()))
    check("the join does not stand out against normal variation",
          max(after) <= internal * 3.0,
          "join %.4f vs internal %.4f" % (max(after), internal))

    # The wrap-around itself: across the join, the coverage map must be no more
    # discontinuous than it is anywhere inside it. That is what np.roll buys, and a
    # stain pasted without it would fail here.
    inside = max(float(np.abs(np.diff(cover, axis=1)).max()),
                 float(np.abs(np.diff(cover, axis=0)).max()))
    across = max(float(np.abs(cover[:, 0] - cover[:, -1]).max()),
                 float(np.abs(cover[0] - cover[-1]).max()))
    check("stains wrap across the edge", across <= inside + 1e-6,
          "across %.4f vs inside %.4f" % (across, inside))
    check("output only darkens", (out <= base + 1e-6).all(),
          "max increase %.4f" % float((out - base).max()))
    check("output actually darkened somewhere", (out < base - 1e-3).any())
    check("output stays in range", out.min() >= 0.0 and out.max() <= 1.0)

    # strength has to be monotonic, or the three variants are not light/medium/heavy
    means = [bake.luma(bake.bake(base, cover, dark, s)).mean() for s in (0.2, 0.5, 0.9)]
    check("stronger settings are darker", means[0] > means[1] > means[2],
          " > ".join("%.4f" % m for m in means))

    # zero coverage must be a no-op, so a wall with no stains is byte-identical
    zero_c = np.zeros((size, size), dtype=np.float32)
    zero_d = np.ones((size, size), dtype=np.float32)
    check("no coverage is a no-op",
          np.allclose(bake.bake(base, zero_c, zero_d, 1.0), base))

    # reproducibility, both within a run and across processes
    again_c, again_d = bake.grime_layer((size, size), stains, seed=7, count=10)
    check("same seed gives the same layer",
          np.array_equal(cover, again_c) and np.array_equal(dark, again_d))
    other_c, _ = bake.grime_layer((size, size), stains, seed=8, count=10)
    check("a different seed gives a different layer", not np.array_equal(cover, other_c))
    check("seed derivation is stable across processes",
          bake.seed_for("TexturesCom_Brick_Modern_1K_albedo_grime1") == 3619154583,
          "got %d" % bake.seed_for("TexturesCom_Brick_Modern_1K_albedo_grime1"))
    check("guids are stable and well formed",
          len(bake.guid_for("x.png")) == 32 and bake.guid_for("x.png") == bake.guid_for("x.png"))
    check("guids differ per asset", bake.guid_for("a.png") != bake.guid_for("b.png"))

    # the material variant: base map swapped, normal map and tiling untouched
    clean = ("Material:\n  m_Name: wall\n  m_SavedProperties:\n    m_TexEnvs:\n"
             "    - _BaseMap:\n        m_Texture: {fileID: 2800000, guid: %s, type: 3}\n"
             "        m_Scale: {x: 2, y: 3}\n        m_Offset: {x: 0.5, y: 0}\n"
             "    - _BumpMap:\n        m_Texture: {fileID: 2800000, guid: %s, type: 3}\n"
             "        m_Scale: {x: 2, y: 3}\n        m_Offset: {x: 0.5, y: 0}\n"
             % ("c" * 32, "n" * 32))
    variant = bake.variant_material(clean, "wall_grime1", "g" * 32)
    check("variant: renamed", "  m_Name: wall_grime1\n" in variant)
    check("variant: base map swapped", "guid: " + "g" * 32 in variant
          and "guid: " + "c" * 32 not in variant)
    check("variant: normal map shared with the clean material", "guid: " + "n" * 32 in variant)
    check("variant: tiling preserved", variant.count("m_Scale: {x: 2, y: 3}") == 2)
    check("variant: offset preserved", variant.count("m_Offset: {x: 0.5, y: 0}") == 2)
    check("variant: no duplicate _BaseMap key", variant.count("    - _BaseMap:\n") == 1)

    print()
    print("GRIMEBAKE TEST %d assertion(s) failed" % len(failures))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
