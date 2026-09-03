#!/usr/bin/env python3
"""Assertions for strip.py, run against synthetic prefabs.

The risk this guards is a partial edit on a 2000-line prefab: an override block that is
not the expected four lines, or a regex that eats one line too many. Both are checked by
refusing to write unless the strict match accounts for every target line and the line
count moves by exactly four per block.

  python3 Tools/StripComponent/test_strip.py
"""
import importlib.util, os, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("strip", os.path.join(HERE, "strip.py"))
strip = importlib.util.module_from_spec(spec)
spec.loader.exec_module(strip)

GUID, FID, OTHER = "1" * 32, "8810266082559041854", "5883217391458024433"
failures = []


def check(label, condition, detail=""):
    if not condition:
        failures.append(label)
    print(("  ok   " if condition else "  FAIL ") + label + ("" if condition else "  " + detail))


SOURCE = """%%YAML 1.1
--- !u!1 &111
GameObject:
  m_Component:
  - component: {fileID: %s}
  - component: {fileID: %s}
  m_Name: Decal Projector
--- !u!4 &%s
Transform:
  m_LocalPosition: {x: 0, y: 0, z: 0}
--- !u!114 &%s
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: deadbeef, type: 3}
  m_Material: {fileID: 2100000, guid: cafe, type: 2}
""" % (OTHER, FID, OTHER, FID)


def override(file_id, prop):
    return ("    - target: {fileID: %s, guid: %s, type: 3}\n"
            "      propertyPath: %s\n"
            "      value: 3\n"
            "      objectReference: {fileID: 0}\n" % (file_id, GUID, prop))


USER = ("--- !u!1001 &222\nPrefabInstance:\n  m_Modifications:\n"
        + override(OTHER, "m_LocalPosition.x")
        + override(FID, "m_Material")
        + override(FID, "m_Size.x")
        + "  m_SourcePrefab: {fileID: 100100000, guid: %s, type: 3}\n" % GUID)


def main():
    with tempfile.TemporaryDirectory() as root:
        prefab = os.path.join(root, "Decal Projector.prefab")
        open(prefab, "w").write(SOURCE)
        open(prefab + ".meta", "w").write("guid: %s\n" % GUID)
        user = os.path.join(root, "user.prefab")
        open(user, "w").write(USER)
        untouched = os.path.join(root, "unrelated.prefab")
        open(untouched, "w").write("--- !u!1 &9\nGameObject:\n  m_Name: nothing\n")
        before_user = open(user).read()

        results = strip.strip_overrides(root, FID, GUID, dry_run=True)
        check("dry run: reports the right file and count", results == [(user, 2)], str(results))
        check("dry run: writes nothing", open(user).read() == before_user)

        strip.strip_overrides(root, FID, GUID, dry_run=False)
        out = open(user).read()
        check("overrides: both component blocks gone", "fileID: %s, guid" % FID not in out)
        check("overrides: the transform override survives", "m_LocalPosition.x" in out)
        check("overrides: the source prefab link survives", "m_SourcePrefab" in out)
        check("overrides: exactly 8 lines removed",
              out.count("\n") == before_user.count("\n") - 8,
              "removed %d" % (before_user.count("\n") - out.count("\n")))
        check("unrelated prefab untouched",
              open(untouched).read() == "--- !u!1 &9\nGameObject:\n  m_Name: nothing\n")

        strip.strip_source(prefab, FID, dry_run=False)
        out = open(prefab).read()
        check("source: component entry removed", "- component: {fileID: %s}" % FID not in out)
        check("source: MonoBehaviour body removed", "MonoBehaviour" not in out)
        check("source: the transform is kept whole",
              "--- !u!4 &%s" % OTHER in out and "m_LocalPosition" in out)
        check("source: the GameObject survives", "m_Name: Decal Projector" in out)
        check("source: its other component entry survives", "- component: {fileID: %s}" % OTHER in out)

        # a malformed override must stop the run rather than half-edit the file
        bad = os.path.join(root, "bad.prefab")
        open(bad, "w").write("  m_Modifications:\n"
                             "    - target: {fileID: %s, guid: %s, type: 3}\n"
                             "      propertyPath: m_Size.x\n" % (FID, GUID))
        try:
            strip.strip_overrides(root, FID, GUID, dry_run=True)
            check("malformed override: refuses to run", False, "no SystemExit raised")
        except SystemExit:
            check("malformed override: refuses to run", True)

    print()
    print("STRIP TEST %d assertion(s) failed" % len(failures))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
