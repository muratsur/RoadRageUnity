#!/usr/bin/env python3
"""Assertions for convert.py, run against synthetic materials rather than the project.

The interesting cases are not the plain one. A slot can point at a texture that is not
in the project (epoxy.mat shipped exactly that), a slot can be present and empty, and a
material can already carry a full set of correctly bound URP slots while only its shader
guid points at HDRP - the two Arabic Neoclassical road materials are like that, with
_MetallicGlossMap and _OcclusionMap holding their own textures rather than the HDRP mask
map. Copying over those would have replaced real maps with a worse approximation.

  python3 Tools/HdrpToUrp/test_convert.py
"""
import importlib.util, os, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("convert", os.path.join(HERE, "convert.py"))
convert = importlib.util.module_from_spec(spec)
spec.loader.exec_module(convert)

REAL, OTHER, MISSING = "a" * 32, "b" * 32, "f" * 32
KNOWN = {REAL, OTHER}

failures = []


def check(label, condition, detail=""):
    if not condition:
        failures.append(label)
    print(("  ok   " if condition else "  FAIL ") + label + ("" if condition else "  " + detail))


def slot(name, guid):
    texture = "{fileID: 2800000, guid: %s, type: 3}" % guid if guid else "{fileID: 0}"
    return ("    - %s:\n        m_Texture: %s\n"
            "        m_Scale: {x: 2, y: 3}\n        m_Offset: {x: 0.5, y: 0}\n"
            % (name, texture))


def material(slots, shader=None):
    return ("Material:\n  m_Name: t\n"
            "  m_Shader: {fileID: 4800000, guid: %s, type: 3}\n"
            "  m_ValidKeywords:\n  - _MATERIAL_FEATURE_X\n"
            "  m_CustomRenderQueue: 2225\n"
            "  disabledShaderPasses:\n  - MOTIONVECTORS\n  - TransparentDepthPrepass\n"
            "  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs:\n"
            % (shader or convert.HDRP_LIT)) + "".join(slots)


def run(directory, name, slots, dry_run=False, shader=None):
    path = os.path.join(directory, name + ".mat")
    open(path, "w").write(material(slots, shader))
    result = convert.convert(path, dry_run, KNOWN)
    return open(path).read(), result, path


def main():
    with tempfile.TemporaryDirectory() as d:
        out, _, _ = run(d, "plain", [slot("_BaseColorMap", REAL), slot("_NormalMap", REAL)])
        check("plain: shader swapped to URP", convert.URP_LIT in out and convert.HDRP_LIT not in out)
        check("plain: _BaseMap added", "    - _BaseMap:\n" in out)
        check("plain: _BumpMap added", "    - _BumpMap:\n" in out)
        check("plain: tiling and offset carried over", out.count("m_Scale: {x: 2, y: 3}") == 4)
        check("plain: _NORMALMAP enabled", "  - _NORMALMAP\n" in out)
        check("plain: HDRP-only keyword dropped", "_MATERIAL_FEATURE_X" not in out)
        check("plain: render queue back to the shader's", "m_CustomRenderQueue: -1" in out)
        check("plain: HDRP passes cleared",
              "disabledShaderPasses: []" in out and "MOTIONVECTORS" not in out)

        out, _, _ = run(d, "dangling", [slot("_BaseColorMap", REAL), slot("_NormalMap", MISSING)])
        check("dangling normal: base map still copied", "    - _BaseMap:\n" in out)
        check("dangling normal: not copied forward", "    - _BumpMap:\n" not in out)
        check("dangling normal: _NORMALMAP left off", "_NORMALMAP" not in out)

        out, _, _ = run(d, "mask", [slot("_BaseColorMap", REAL), slot("_MaskMap", REAL)])
        check("mask map: feeds _MetallicGlossMap", "    - _MetallicGlossMap:\n" in out)
        check("mask map: feeds _OcclusionMap", "    - _OcclusionMap:\n" in out)
        check("mask map: both keywords enabled",
              "_METALLICSPECGLOSSMAP" in out and "_OCCLUSIONMAP" in out)

        out, _, _ = run(d, "both", [slot("_BaseColorMap", REAL), slot("_BaseMap", OTHER),
                                    slot("_MaskMap", REAL), slot("_MetallicGlossMap", OTHER),
                                    slot("_OcclusionMap", OTHER)])
        check("already URP: existing bindings win", out.count("guid: " + OTHER) == 3,
              "found %d" % out.count("guid: " + OTHER))
        check("already URP: no duplicate _BaseMap", out.count("    - _BaseMap:\n") == 1)
        check("already URP: no duplicate _MetallicGlossMap",
              out.count("    - _MetallicGlossMap:\n") == 1)
        check("already URP: keywords match what is bound",
              "_METALLICSPECGLOSSMAP" in out and "_OCCLUSIONMAP" in out)

        out, _, _ = run(d, "empty", [slot("_NormalMap", REAL), slot("_BumpMap", None)])
        check("empty slot: not duplicated", out.count("    - _BumpMap:\n") == 1)
        check("empty slot: keyword stays off", "_NORMALMAP" not in out)

        _, result, path = run(d, "urp", [], shader=convert.URP_LIT)
        before = open(path).read()
        check("already on URP: reported as nothing to do", result is None)
        check("already on URP: left byte for byte", open(path).read() == before)

        binary = os.path.join(d, "bin.mat")
        open(binary, "wb").write(b"\x00\x95\xfeMaterial")
        check("binary material: reported, not crashed",
              convert.convert(binary, False, KNOWN) == "binary")

        path = os.path.join(d, "dry.mat")
        open(path, "w").write(material([slot("_BaseColorMap", REAL)]))
        before = open(path).read()
        convert.convert(path, True, KNOWN)
        check("dry run: writes nothing", open(path).read() == before)

    print()
    print("HDRP2URP TEST %d assertion(s) failed" % len(failures))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
