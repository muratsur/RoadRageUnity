#!/usr/bin/env python3
"""Restore the texture bindings a stripped copy of a material lost.

WHY

The NYC and USA buildings the game actually loads are FBX files under
Assets/Resources/Buildings. Their importers use external materials with a recursive-up
name search (materialLocation 0, materialSearch 1), so Unity resolves each submesh
against the Materials folder beside the FBX - not against the source pack's Materials
folder, which nothing at runtime ever reads.

Those resolved materials are stripped. TexturesCom_Brick_Modern_1K_albedo.mat is on
URP/Lit with an empty _BaseMap and a white _BaseColor, and 24 of them across the two
biomes are the same. So every wall arrives flat white and whatever tint the material
pass applies is the entire surface. That is why the city reads as one flat colour per
building rather than as architecture, and no work on the source pack's materials could
have changed it, because those are not the materials being used.

HOW IT DECIDES

Each pack ships an intact twin under its own Models/Materials with the same asset name -
that is what the FBX was authored against. So the rule is material to material, never
material to texture: for a stripped material named N, find the other materials named N
whose _BaseMap is bound, and copy the base map, the normal map and the tiling.

Matching on texture name instead would be actively unsafe here. The stripped set
includes diffuse.mat, and the only texture asset in the project named "diffuse" is a
tree bark png in an unrelated pack. The twins agree on the pack's own diffuse texture.

The two slots are resolved independently, because a twin that has no normal map is not
disagreeing about the normal map, it just has nothing to say. Both packs bind the same
brick albedo at the same tiling and only the USA one carries the normal, so the brick
resolves cleanly. Twins that genuinely contradict each other on a slot are skipped and
reported rather than guessed at, and a slot that is already bound is never overwritten.

  python3 Tools/MaterialTextures/rebind.py <dir> [--project Assets] [--dry-run]
"""
import argparse, os, re, sys

SLOT = (r"^    - %s:\n^        m_Texture: \{(?P<t>[^}]*)\}\n"
        r"^        m_Scale: \{(?P<s>[^}]*)\}\n^        m_Offset: \{(?P<o>[^}]*)\}\n")


def slot(source, name):
    m = re.search(SLOT % re.escape(name), source, re.M)
    return m.groupdict() if m else None


def is_bound(block):
    return block is not None and "fileID: 0}" not in "{%s}" % block["t"]


def read(path):
    try:
        return open(path, encoding="utf-8").read()
    except (UnicodeDecodeError, OSError):
        return None


def collect_twins(project):
    """Bound materials by asset name -> the distinct bindings seen under that name."""
    twins = {}
    for directory, _, files in os.walk(project):
        for name in sorted(files):
            if not name.endswith(".mat"):
                continue
            path = os.path.join(directory, name)
            source = read(path)
            if source is None:
                continue
            base = slot(source, "_BaseMap")
            if not is_bound(base):
                continue
            bump = slot(source, "_BumpMap")
            entry = twins.setdefault(name[:-4], {"base": {}, "bump": {}})
            entry["base"].setdefault((base["t"], base["s"], base["o"]), path)
            if is_bound(bump):
                entry["bump"].setdefault(bump["t"], path)
    return twins


def rebind(path, twins, dry_run):
    source = read(path)
    if source is None:
        return None
    stem = os.path.basename(path)[:-4]
    if is_bound(slot(source, "_BaseMap")):
        return None  # already has one; never overwrite

    entry = twins.get(stem)
    if not entry:
        return None
    bases = {k: p for k, p in entry["base"].items() if p != path}
    bumps = {k: p for k, p in entry["bump"].items() if p != path}
    if not bases:
        return None
    if len(bases) > 1:
        return "%-46s SKIPPED - %d twins disagree on _BaseMap" % (stem, len(bases))
    if len(bumps) > 1:
        return "%-46s SKIPPED - %d twins disagree on _BumpMap" % (stem, len(bumps))

    (texture, scale, offset), twin = next(iter(bases.items()))
    bump = next(iter(bumps)) if bumps else None
    updated = re.sub(SLOT % "_BaseMap",
                     "    - _BaseMap:\n        m_Texture: {%s}\n"
                     "        m_Scale: {%s}\n        m_Offset: {%s}\n" % (texture, scale, offset),
                     source, count=1, flags=re.M)

    notes = ["_BaseMap"]
    if bump and not is_bound(slot(source, "_BumpMap")):
        updated = re.sub(SLOT % "_BumpMap",
                         "    - _BumpMap:\n        m_Texture: {%s}\n"
                         "        m_Scale: {%s}\n        m_Offset: {%s}\n" % (bump, scale, offset),
                         updated, count=1, flags=re.M)
        if "  - _NORMALMAP\n" not in updated:
            updated = re.sub(r"^  m_ValidKeywords:(?:\n  - .*)*\n|^  m_ValidKeywords: \[\]\n",
                             "  m_ValidKeywords:\n  - _NORMALMAP\n", updated, count=1, flags=re.M)
        notes.append("_BumpMap")

    if not dry_run:
        open(path, "w", encoding="utf-8", newline="").write(updated)
    return "%-46s %-18s tiling {%s}  from %s" % (
        stem, "+".join(notes), scale, os.path.relpath(twin))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("target", help="directory of stripped materials to restore")
    parser.add_argument("--project", default="Assets", help="where to look for twins")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    twins = collect_twins(args.project)
    changed = skipped = 0
    for directory, _, files in os.walk(args.target):
        for name in sorted(files):
            if not name.endswith(".mat"):
                continue
            note = rebind(os.path.join(directory, name), twins, args.dry_run)
            if not note:
                continue
            print("  " + note)
            if "SKIPPED" in note:
                skipped += 1
            else:
                changed += 1
    print("REBIND %s %d material(s), skipped %d" % (
        "would change" if args.dry_run else "changed", changed, skipped))
    return 0


if __name__ == "__main__":
    sys.exit(main())
