using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VTuberMyAvatar
{
    /// <summary>
    /// One concrete blendshape on a specific mesh.
    /// </summary>
    public struct ShapeRef
    {
        public SkinnedMeshRenderer Renderer;
        public int Index;
    }

    /// <summary>
    /// Discovers every blendshape under an avatar and resolves friendly candidate
    /// names (ARKit / Unified Expressions / viseme) to concrete mesh shapes.
    ///
    /// Matching is tolerant: names are normalised (lower-case, separators removed)
    /// and each shape is also indexed by the part after a "." prefix, so
    /// "Facial_Blends.JawOpen" and "vrc.v_aa" resolve from "JawOpen" / "v_aa".
    /// </summary>
    public class BlendshapeLibrary
    {
        private readonly Dictionary<string, List<ShapeRef>> _byKey =
            new Dictionary<string, List<ShapeRef>>();

        public List<SkinnedMeshRenderer> Renderers { get; } = new List<SkinnedMeshRenderer>();
        public int TotalShapeCount { get; private set; }

        public BlendshapeLibrary(GameObject avatarRoot)
        {
            var smrs = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                var mesh = smr.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                Renderers.Add(smr);
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string raw = mesh.GetBlendShapeName(i);
                    TotalShapeCount++;
                    var refr = new ShapeRef { Renderer = smr, Index = i };
                    AddKey(Normalize(raw), refr);
                    int dot = raw.LastIndexOf('.');
                    if (dot >= 0 && dot < raw.Length - 1)
                        AddKey(Normalize(raw.Substring(dot + 1)), refr);
                }
            }
        }

        private void AddKey(string key, ShapeRef refr)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!_byKey.TryGetValue(key, out var list))
            {
                list = new List<ShapeRef>(1);
                _byKey[key] = list;
            }
            // Avoid duplicate (same renderer+index) under one key.
            foreach (var r in list)
                if (r.Renderer == refr.Renderer && r.Index == refr.Index) return;
            list.Add(refr);
        }

        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c == '.' || c == '_' || c == '-' || c == ' ') continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns the concrete shapes for the first candidate name that exists,
        /// or null if none match. A single name can resolve to several meshes
        /// (e.g. body + a separate face/teeth mesh that share the shape).
        /// </summary>
        public List<ShapeRef> Resolve(IEnumerable<string> candidateNames)
        {
            foreach (var cand in candidateNames)
            {
                if (_byKey.TryGetValue(Normalize(cand), out var list))
                    return list;
            }
            return null;
        }

        public bool Has(string candidateName) => _byKey.ContainsKey(Normalize(candidateName));

        /// <summary>Human-readable dump of every discovered shape, for diagnostics.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append($"[vTuber] {Renderers.Count} skinned mesh(es), {TotalShapeCount} blendshapes:\n");
            foreach (var smr in Renderers)
            {
                var mesh = smr.sharedMesh;
                sb.Append($"  - {smr.name}: {mesh.blendShapeCount} shapes\n");
            }
            return sb.ToString();
        }
    }
}
