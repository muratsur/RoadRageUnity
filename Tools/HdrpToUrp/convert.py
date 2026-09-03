#!/usr/bin/env python3
"""Rewrite HDRP/Lit materials as URP/Lit, in place.

WHY

This project renders with URP. GraphicsSettings points m_CustomRenderPipeline at
Assets/Settings/RoadRageURP.asset, a UniversalRenderPipelineAsset, and the global
settings map holds one entry, UniversalRenderPipeline. A material on the HDRP/Lit
shader therefore does not draw, and installing the HDRP package does not change that:
only one pipeline is active at a time, HDRP/Lit is compiled for HDRP's passes and
lighting data, and URP has neither. Two pipeline packages in one project is a state
Unity warns about rather than a way to mix their shaders.

So the fix is the material, not the package. 29 of them point at HDRP/Lit: 27 in the
NYC buildings pack, where Prefabs/Parts/building_*_middle - the sections the NYCVariants
towers stack - reference seven apiece, and two Arabic Neoclassical road materials. That
is why BiomeModel replaces every one of them with a flat colour, and why Manhattan reads
as untextured.

The textures are fine and already imported: brick, plaster, brushed steel, copper, old
wood, epoxy paint, bitumen roofing. Only the material assets are wrong.

WHAT IT CHANGES

  m_Shader                6e4ae406... (HDRP/Lit)  ->  933532a4... (URP/Lit)
  _BaseColorMap           copied to _BaseMap, with its tiling and offset
  _NormalMap              copied to _BumpMap, with its tiling and offset
  _MaskMap                copied to _MetallicGlossMap and _OcclusionMap - HDRP packs
                          metallic/AO/detail/smoothness into RGBA and URP reads R+A
                          from the first and G from the second, so it is the same
                          texture read twice, not a repack
  m_ValidKeywords         HDRP set -> the keywords for the maps actually bound
  disabledShaderPasses    HDRP pass names -> empty
  m_CustomRenderQueue     2225 (HDRP opaque) -> -1, meaning "take it from the shader"

Height maps are deliberately not carried across. HDRP drives displacement from
_HeightAmplitude in metres and URP from _Parallax in a 0.005-0.08 band, so there is no
honest conversion, and the two road materials that bind one set _HeightAmplitude to
0.0001 - a tenth of a millimetre, which is no displacement at all. Guessing a URP
amplitude would invent an effect the author did not ask for.

Nothing is deleted. HDRP-only properties (_AnisotropyMap, _DiffusionProfileAsset and
the rest) are left where they are: URP ignores properties its shader does not declare,
so removing them would be risk without benefit, and leaving them means this is
reversible by swapping the shader guid back.

Run with --dry-run to see the plan without writing.
"""
import argparse, os, re, sys

HDRP_LIT = "6e4ae4064600d784cac1e41a9e6f2e59"
URP_LIT = "933532a4fcc9baf4fa0491de14d08ed7"

# HDRP slot -> the URP slots it feeds, each with the keyword that switches it on.
# Copied, not renamed, so the original stays for reference.
#
# _MaskMap is one texture in HDRP and two in URP, but no channel is resampled:
# HDRP packs metallic in R, ambient occlusion in G, detail in B and smoothness in A,
# and URP reads metallic from R and smoothness from A of _MetallicGlossMap and
# occlusion from G of _OcclusionMap. Pointing both at the same texture is therefore
# the same data, read twice. B is simply unused.
SLOTS = {
    "_BaseColorMap": [("_BaseMap", None)],
    "_NormalMap": [("_BumpMap", "_NORMALMAP")],
    "_MaskMap": [("_MetallicGlossMap", "_METALLICSPECGLOSSMAP"),
                 ("_OcclusionMap", "_OCCLUSIONMAP")],
}


def texture_block(source, slot):
    """The three lines Unity serialises for one texture slot, or None if unbound."""
    m = re.search(
        r"^    - %s:\n"
        r"^        m_Texture: \{(?P<tex>[^}]*)\}\n"
        r"^        m_Scale: \{(?P<scale>[^}]*)\}\n"
        r"^        m_Offset: \{(?P<offset>[^}]*)\}\n" % re.escape(slot),
        source, re.M)
    if not m or "fileID: 0}" in "{%s}" % m.group("tex"):
        return None
    return m.group("tex"), m.group("scale"), m.group("offset")


def project_texture_guids(root="Assets"):
    """Every asset guid in the project, so a slot pointing at a texture that no longer
    exists is left unbound rather than copied forward. epoxy.mat shipped with exactly
    that: a dangling normal map. Binding it and enabling _NORMALMAP would have URP
    sampling nothing and lighting the surface off a wrong normal."""
    guids = set()
    for directory, _, files in os.walk(root):
        for name in files:
            if not name.endswith(".meta"):
                continue
            head = open(os.path.join(directory, name), encoding="utf-8",
                        errors="ignore").read(400)
            found = re.search(r"guid: ([a-f0-9]{32})", head)
            if found:
                guids.add(found.group(1))
    return guids


def bound(source, slot, known_guids):
    """Is this slot pointing at a texture that actually exists? A slot can be present
    and empty, and it can be present and dangling - epoxy.mat shipped a normal map guid
    that is not in the project. Neither counts as bound, because enabling the keyword
    for one would have URP light the surface off a texture it cannot sample."""
    block = texture_block(source, slot)
    if block is None:
        return False
    guid = re.search(r"guid: ([a-f0-9]{32})", block[0])
    return not (known_guids is not None and guid and guid.group(1) not in known_guids)


def convert(path, dry_run, known_guids=None):
    try:
        source = open(path, encoding="utf-8").read()
    except UnicodeDecodeError:
        return "binary"  # a binary-serialised material; no text to rewrite
    if HDRP_LIT not in source:
        return None

    # Some packs ship a material that carries both slot sets - the Arabic Neoclassical
    # road materials have _BaseMap, _BumpMap, _MetallicGlossMap and _OcclusionMap
    # already filled in, each with its own texture, and only the shader guid points at
    # HDRP. Copying over those would replace real URP maps with the HDRP packing, so an
    # existing binding always wins and only empty slots are filled from the HDRP side.
    copied = []
    additions = ""
    for hdrp_slot, targets in SLOTS.items():
        source_bound = bound(source, hdrp_slot, known_guids)
        for urp_slot, _ in targets:
            if bound(source, urp_slot, known_guids) or not source_bound:
                continue
            if re.search(r"^    - %s:" % re.escape(urp_slot), source, re.M):
                continue  # present but empty, and Unity will not tolerate a duplicate
            tex, scale, offset = texture_block(source, hdrp_slot)
            additions += ("    - %s:\n        m_Texture: {%s}\n"
                          "        m_Scale: {%s}\n        m_Offset: {%s}\n"
                          % (urp_slot, tex, scale, offset))
            copied.append("%s->%s" % (hdrp_slot, urp_slot))

    updated = source.replace(HDRP_LIT, URP_LIT)
    if additions:
        updated = updated.replace("    m_TexEnvs:\n", "    m_TexEnvs:\n" + additions, 1)

    # Keywords follow what the URP slots ended up holding, not what the HDRP side had.
    enabled = sorted({keyword for targets in SLOTS.values() for urp_slot, keyword in targets
                      if keyword and bound(updated, urp_slot, known_guids)})
    keywords = ("  m_ValidKeywords:\n" + "".join("  - %s\n" % k for k in enabled)
                if enabled else "  m_ValidKeywords: []\n")
    updated = re.sub(r"^  m_ValidKeywords:(?:\n  - .*)*\n|^  m_ValidKeywords: \[\]\n",
                     keywords, updated, count=1, flags=re.M)
    updated = re.sub(r"^  disabledShaderPasses:(?:\n  - .*)*\n|^  disabledShaderPasses: \[\]\n",
                     "  disabledShaderPasses: []\n", updated, count=1, flags=re.M)
    updated = re.sub(r"^  m_CustomRenderQueue: -?\d+$", "  m_CustomRenderQueue: -1",
                     updated, count=1, flags=re.M)

    if not dry_run:
        open(path, "w", encoding="utf-8", newline="").write(updated)
    return copied + ["+%s" % k for k in enabled]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", default="Assets")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    known_guids = project_texture_guids()
    converted = 0
    binary = 0
    for directory, _, files in os.walk(args.root):
        for name in sorted(files):
            if not name.endswith(".mat"):
                continue
            path = os.path.join(directory, name)
            copied = convert(path, args.dry_run, known_guids)
            if copied is None:
                continue
            if copied == "binary":
                binary += 1
                continue
            converted += 1
            print("  %-46s %s" % (name[:-4], ", ".join(copied) or "shader only"))
    verb = "would convert" if args.dry_run else "converted"
    print("HDRP2URP %s %d material(s)" % (verb, converted))
    if binary:
        print("HDRP2URP skipped %d binary-serialised material(s)" % binary)
    return 0


if __name__ == "__main__":
    sys.exit(main())
