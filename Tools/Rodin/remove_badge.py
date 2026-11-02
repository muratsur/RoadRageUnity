"""Paints makers' badges off a Rodin vehicle: Rodin adds them whatever the prompt says.

    blender -b -P Tools/Rodin/remove_badge.py -- <source.fbx> <out.fbx> <texture_diffuse.png> <patch> [patch...]

A patch is front|rear:height:half width:half height[:grey], all as shares of the vehicle's
height and width, on the middle of that end. The badge is modelled as well as painted, so
inside the patch the mesh is pressed flat and written to out.fbx, the normal map beside the
texture goes flat, and the colour becomes that grey, or that of the texels just around the
patch. The vehicle faces -Y in Rodin's file. Textures are changed in place.
"""
import sys

import bpy
import numpy as np
from mathutils import Vector

source, out_fbx, path, *patches = sys.argv[sys.argv.index("--") + 1:]
AROUND = 1.4
END_DEPTH = 0.25
SHELL = 0.02

bpy.ops.import_scene.fbx(filepath=source)
ob = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.polygons))
me = ob.data
world = ob.matrix_world
rotation = world.to_3x3().normalized()
verts = np.array([(world @ v.co)[:] for v in me.vertices])
lo, hi = verts.min(0), verts.max(0)
size = hi - lo

image = bpy.data.images.load(path)
w, h = image.size
pixels = np.empty(w * h * 4, np.float32)
image.pixels.foreach_get(pixels)
pixels = pixels.reshape(h, w, 4)
uv = me.uv_layers.active.data
normal_path = path.replace("texture_diffuse", "texture_normal")
normal_image = bpy.data.images.load(normal_path)
normal_pixels = np.empty(w * h * 4, np.float32)
normal_image.pixels.foreach_get(normal_pixels)
normal_pixels = normal_pixels.reshape(h, w, 4)
to_local = world.inverted()

for patch in patches:
    end, height, half_w, half_h, *grey = patch.split(":")
    height, half_w, half_h = float(height), float(half_w), float(half_h)
    sign = -1 if end == "front" else 1
    inside = np.zeros((h, w), bool)
    ring = np.zeros((h, w), bool)
    for p in me.polygons:
        normal = (rotation @ p.normal).normalized()
        if normal.y * sign < 0.2:
            continue
        ids = list(p.vertices)
        along = (verts[ids, 1].mean() - lo[1]) / size[1]
        if (along if sign < 0 else 1 - along) > END_DEPTH:
            continue
        loops = list(p.loop_indices)
        for k in range(1, len(loops) - 1):
            corners = [loops[0], loops[k], loops[k + 1]]
            ax, ay, bx, by, cx, cy = [c for l in corners for c in (uv[l].uv[0] * w, uv[l].uv[1] * h)]
            d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
            if abs(d) < 1e-6:
                continue
            x0, x1 = max(0, int(min(ax, bx, cx)) - 1), min(w, int(max(ax, bx, cx)) + 2)
            y0, y1 = max(0, int(min(ay, by, cy)) - 1), min(h, int(max(ay, by, cy)) + 2)
            if x1 <= x0 or y1 <= y0:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            a = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / d
            b = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / d
            c = 1 - a - b
            on = (a >= -0.05) & (b >= -0.05) & (c >= -0.05)
            ends = verts[[me.loops[l].vertex_index for l in corners]]
            px = (a * ends[0, 0] + b * ends[1, 0] + c * ends[2, 0] - (lo[0] + hi[0]) / 2) / size[0]
            pz = (a * ends[0, 2] + b * ends[1, 2] + c * ends[2, 2] - lo[2]) / size[2] - height
            near = on & (np.abs(px) < half_w) & (np.abs(pz) < half_h)
            wide = on & (np.abs(px) < half_w * AROUND) & (np.abs(pz) < half_h * AROUND)
            inside[y0:y1, x0:x1] |= near
            ring[y0:y1, x0:x1] |= wide & ~near
    ring &= ~inside
    if grey:
        pixels[..., :3][inside] = float(grey[0])
    elif inside.any() and ring.any():
        pixels[..., :3][inside] = np.median(pixels[..., :3][ring], axis=0)
    normal_pixels[..., :3][inside] = (0.5, 0.5, 1.0)
    # Press the modelled badge flat, onto the surface it stands on.
    along = (verts[:, 1] - lo[1]) / size[1]
    at_end = along < END_DEPTH if sign < 0 else along > 1 - END_DEPTH
    px = np.abs((verts[:, 0] - (lo[0] + hi[0]) / 2) / size[0])
    pz = np.abs((verts[:, 2] - lo[2]) / size[2] - height)
    chosen = np.nonzero(at_end & (px < half_w) & (pz < half_h))[0]
    if len(chosen):
        # Only the shell: moving what lies behind it pulled spikes through the surface.
        plane = np.median(verts[chosen, 1])
        for i in chosen:
            if abs(verts[i, 1] - plane) > SHELL * size[1]:
                continue
            verts[i, 1] = plane
            me.vertices[i].co = to_local @ Vector(verts[i])
    print("BADGE", path[-40:], patch, "texels", int(inside.sum()))

image.pixels.foreach_set(pixels.ravel())
image.filepath_raw = path
image.file_format = "PNG"
image.save()
normal_image.pixels.foreach_set(normal_pixels.ravel())
normal_image.filepath_raw = normal_path
normal_image.file_format = "PNG"
normal_image.save()
me.update()
for o in list(bpy.data.objects):
    if o is not ob:
        bpy.data.objects.remove(o)
bpy.ops.object.select_all(action="DESELECT")
ob.select_set(True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, path_mode="STRIP", embed_textures=False)
