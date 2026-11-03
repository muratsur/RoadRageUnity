using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// What is that? In the editor (and development builds), press F9 or click the middle
    /// mouse button over anything in the Game view - a white line through the forest, a
    /// bush that popped in - and the console lists what is drawn under the mouse, nearest
    /// first, as RR_PICK: the object's path in the hierarchy, its material, its size and
    /// how far away it is. Works with the game paused from its own PAUSE button.
    ///
    /// Renderers are tested by their triangles where the mesh can be read (everything the
    /// game builds at runtime), otherwise by their bounds, which is marked.
    public sealed class RoadRagePicker : MonoBehaviour
    {
        private void Update()
        {
            if (!Pressed()) return;
            var camera = Camera.main;
            if (camera == null) return;
            var ray = camera.ScreenPointToRay(MousePosition());
            var hits = new List<(float distance, Renderer renderer, bool exact)>();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!renderer.bounds.IntersectRay(ray, out var boundsDistance)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh != null && mesh.isReadable)
                {
                    if (RayMesh(ray, mesh, renderer.transform.localToWorldMatrix, out var exact))
                        hits.Add((exact, renderer, true));
                }
                else
                {
                    hits.Add((boundsDistance, renderer, false));
                }
            }
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));
            var log = new StringBuilder($"RR_PICK under the mouse ({hits.Count} found, nearest first):");
            for (var i = 0; i < Mathf.Min(10, hits.Count); i++)
            {
                var (distance, renderer, exact) = hits[i];
                var material = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none";
                var size = renderer.bounds.size;
                log.Append($"\n  {distance:0.0} m{(exact ? "" : " (by its bounds)")}: '{PathOf(renderer.transform)}' " +
                           $"material '{material}', layer {LayerMask.LayerToName(renderer.gameObject.layer)}, " +
                           $"size {size.x:0.#} x {size.y:0.#} x {size.z:0.#} m");
            }
            Debug.Log(log.ToString());
        }

        private static bool Pressed()
        {
            try
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null && keyboard.f9Key.wasPressedThisFrame) return true;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null && mouse.middleButton.wasPressedThisFrame) return true;
            }
            catch { }
            try
            {
                if (Input.GetKeyDown(KeyCode.F9) || Input.GetMouseButtonDown(2)) return true;
            }
            catch { }
            return false;
        }

        private static Vector3 MousePosition()
        {
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null) return mouse.position.ReadValue();
            }
            catch { }
            try
            {
                return Input.mousePosition;
            }
            catch { }
            return new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        }

        private static string PathOf(Transform transform)
        {
            var path = transform.name;
            for (var t = transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            return path;
        }

        /// Nearest hit of the ray on the mesh's triangles (both faces), in world space.
        private static bool RayMesh(Ray ray, Mesh mesh, Matrix4x4 toWorld, out float nearest)
        {
            nearest = float.PositiveInfinity;
            var vertices = mesh.vertices;
            var world = new Vector3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++) world[i] = toWorld.MultiplyPoint3x4(vertices[i]);
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var triangles = mesh.GetTriangles(sub);
                for (var t = 0; t + 2 < triangles.Length; t += 3)
                {
                    if (RayTriangle(ray, world[triangles[t]], world[triangles[t + 1]], world[triangles[t + 2]],
                            out var distance) && distance < nearest)
                        nearest = distance;
                }
            }
            return !float.IsInfinity(nearest);
        }

        /// Moller-Trumbore, both faces.
        private static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            var ab = b - a;
            var ac = c - a;
            var p = Vector3.Cross(ray.direction, ac);
            var det = Vector3.Dot(ab, p);
            if (Mathf.Abs(det) < 1e-8f) return false;
            var inverse = 1f / det;
            var s = ray.origin - a;
            var u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, ab);
            var v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(ac, q) * inverse;
            return distance > 0f;
        }
    }
}
