#!/usr/bin/env python3
"""Remove a component from a prefab, and every override that targeted it.

WHY

Prefabs/Decal Projector.prefab carries an HDRP DecalProjector, script guid
f19d9143a39eb3b46bc4563e9889cfbd. HDRP is not in this project, so that component is a
missing script: it renders nothing and Unity logs it. The prefab is nested 238 times
across the NYC building sections, and NYCVariants stacks those sections into every tower
in the biome, so the missing script is instantiated over and over at runtime.

Making the decals draw instead would mean adding URP's Decal Renderer Feature to
RoadRageRenderer, which is a screen-space pass on top of the screen-space ambient
occlusion already there, and then 238 live projectors per stacked section. The decals
are leaking stains, wall grime and fallen leaves. That is a lot of frame time for
weathering nobody has ever seen in this project, in a biome already over its triangle
budget, so the component goes and the assets go with it.

HOW

Editing the one source prefab fixes all 238 instances at once, because they are prefab
instances of it rather than copies. The instances do keep an empty GameObject each,
which costs a Transform and nothing else.

The overrides are the fiddly part. Each nesting prefab stores its own settings for the
component as four-line blocks under m_Modifications, and those would be left pointing at
a component that no longer exists - including objectReference entries naming decal
materials that are being deleted. So they are removed too. The pattern is anchored on
both the component's fileID and the source prefab's guid, and the tool refuses to run
unless a strict four-line match accounts for every target line it can find.

  python3 Tools/StripComponent/strip.py <prefab> <fileID> [--dry-run]
"""
import argparse, os, re, sys


def blocks(file_id, guid):
    """The four lines Unity writes for one override of one component, and a loose match
    on just the target line, so the two counts can be compared before anything moves."""
    strict = re.compile(
        r"^    - target: \{fileID: %s, guid: %s, type: 3\}\n"
        r"^      propertyPath: .*\n"
        r"^      value: ?.*\n"
        r"^      objectReference: \{[^}]*\}\n" % (file_id, guid), re.M)
    loose = re.compile(
        r"^    - target: \{fileID: %s, guid: %s, type: 3\}$" % (file_id, guid), re.M)
    return strict, loose


def strip_source(path, file_id, dry_run):
    """Drop the component from the prefab that declares it."""
    source = open(path, encoding="utf-8").read()
    entry = "  - component: {fileID: %s}\n" % file_id
    if source.count(entry) != 1:
        raise SystemExit("expected one component entry for %s in %s, found %d"
                         % (file_id, path, source.count(entry)))
    body = re.compile(r"^--- !u!\d+ &%s\n(?:(?!^--- ).*\n)*" % file_id, re.M)
    if len(body.findall(source)) != 1:
        raise SystemExit("expected one body block for %s in %s" % (file_id, path))
    updated = body.sub("", source.replace(entry, "", 1))
    if not dry_run:
        open(path, "w", encoding="utf-8", newline="").write(updated)
    return True


def strip_overrides(root, file_id, guid, dry_run):
    strict, loose = blocks(file_id, guid)
    results = []
    for directory, _, files in os.walk(root):
        for name in sorted(files):
            if not (name.endswith(".prefab") or name.endswith(".unity")):
                continue
            path = os.path.join(directory, name)
            try:
                source = open(path, encoding="utf-8").read()
            except (UnicodeDecodeError, OSError):
                continue
            found, targets = len(strict.findall(source)), len(loose.findall(source))
            if targets == 0:
                continue
            if found != targets:
                raise SystemExit("%s: %d four-line overrides but %d target lines - "
                                 "the shape is not what this tool expects, refusing"
                                 % (path, found, targets))
            updated = strict.sub("", source)
            if updated.count("\n") != source.count("\n") - 4 * found:
                raise SystemExit("%s: removed %d blocks but the line count moved by %d"
                                 % (path, found, source.count("\n") - updated.count("\n")))
            if not dry_run:
                open(path, "w", encoding="utf-8", newline="").write(updated)
            results.append((path, found))
    return results


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("prefab", help="the prefab that declares the component")
    parser.add_argument("file_id", help="the component's fileID inside that prefab")
    parser.add_argument("--root", default="Assets", help="where to strip overrides")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    meta = open(args.prefab + ".meta", encoding="utf-8").read()
    guid = re.search(r"guid: ([a-f0-9]{32})", meta).group(1)

    results = strip_overrides(args.root, args.file_id, guid, args.dry_run)
    total = sum(n for _, n in results)
    for path, n in sorted(results, key=lambda r: -r[1]):
        print("  %5d  %s" % (n, path))
    strip_source(args.prefab, args.file_id, args.dry_run)
    print("STRIP %s component %s from %s, and %d override(s) in %d file(s)"
          % ("would remove" if args.dry_run else "removed",
             args.file_id, os.path.basename(args.prefab), total, len(results)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
