#!/usr/bin/env python3
"""Assertions for rebind.py, run against synthetic materials rather than the project.

The case that matters is the one that made a texture-name match unusable: a stripped
material called diffuse.mat, and somewhere else in the project a texture asset also
called diffuse that belongs to something entirely unrelated. Resolution has to come from
a twin material, never from a same-named texture.

  python3 Tools/MaterialTextures/test_rebind.py
"""
import importlib.util, os, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("rebind", os.path.join(HERE, "rebind.py"))
rebind = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rebind)

A, B, N = "a" * 32, "b" * 32, "n" * 32
failures = []


def check(label, condition, detail=""):
    if not condition:
        failures.append(label)
    print(("  ok   " if condition else "  FAIL ") + label + ("" if condition else "  " + detail))


def slot(name, guid, scale="x: 1, y: 1"):
    texture = "{fileID: 2800000, guid: %s, type: 3}" % guid if guid else "{fileID: 0}"
    return ("    - %s:\n        m_Texture: %s\n"
            "        m_Scale: {%s}\n        m_Offset: {x: 0, y: 0}\n" % (name, texture, scale))


def write(path, base, bump, scale="x: 1, y: 1"):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, "w").write(
        "Material:\n  m_Name: x\n  m_ValidKeywords: []\n"
        "  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs:\n"
        + slot("_BaseMap", base, scale) + slot("_BumpMap", bump, scale))
    return path


def main():
    with tempfile.TemporaryDirectory() as root:
        src, dst = os.path.join(root, "pack"), os.path.join(root, "res")

        # a stripped material, its intact twin, and a decoy texture with the same name
        write(os.path.join(src, "brick.mat"), A, N)
        stripped = write(os.path.join(dst, "brick.mat"), None, None)
        write(os.path.join(src, "solo.mat"), A, None)          # twin with no normal map
        solo = write(os.path.join(dst, "solo.mat"), None, None)
        orphan = write(os.path.join(dst, "orphan.mat"), None, None)   # no twin anywhere
        write(os.path.join(src, "kept.mat"), A, N)
        kept = write(os.path.join(dst, "kept.mat"), B, None)    # already bound
        # two twins that truly disagree on the base map
        write(os.path.join(src, "clash.mat"), A, None)
        write(os.path.join(root, "other", "clash.mat"), B, None)
        clash = write(os.path.join(dst, "clash.mat"), None, None)
        # a texture asset sharing a stripped material's name must be ignored
        os.makedirs(os.path.join(root, "trees"), exist_ok=True)
        open(os.path.join(root, "trees", "brick.png"), "w").write("x")
        open(os.path.join(root, "trees", "brick.png.meta"), "w").write("guid: %s\n" % ("d" * 32))

        twins = rebind.collect_twins(root)

        note = rebind.rebind(stripped, twins, False)
        out = open(stripped).read()
        check("stripped: base map restored from twin", "guid: " + A in out)
        check("stripped: normal map restored", "guid: " + N in out)
        check("stripped: _NORMALMAP enabled", "  - _NORMALMAP\n" in out)
        check("stripped: decoy texture of the same name ignored", "guid: " + "d" * 32 not in out)

        rebind.rebind(solo, twins, False)
        out = open(solo).read()
        check("twin without a normal: base restored", "guid: " + A in out)
        check("twin without a normal: keyword stays off", "_NORMALMAP" not in out)

        check("no twin: left alone", rebind.rebind(orphan, twins, False) is None)

        before = open(kept).read()
        check("already bound: not overwritten", rebind.rebind(kept, twins, False) is None)
        check("already bound: byte for byte", open(kept).read() == before)

        note = rebind.rebind(clash, twins, False)
        check("contradicting twins: skipped", note is not None and "SKIPPED" in note, str(note))
        check("contradicting twins: nothing written", "fileID: 0" in open(clash).read())

        # dry run
        again = write(os.path.join(dst, "dry.mat"), None, None)
        write(os.path.join(src, "dry.mat"), A, N)
        twins = rebind.collect_twins(root)
        before = open(again).read()
        rebind.rebind(again, twins, True)
        check("dry run: writes nothing", open(again).read() == before)

    print()
    print("REBIND TEST %d assertion(s) failed" % len(failures))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
