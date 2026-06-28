using System.Collections.Generic;
using UnityEngine;

namespace VTuberMyAvatar
{
    /// <summary>
    /// Build-safe Verlet spring-bone chain for hair / clothing / ear secondary motion,
    /// a standalone replacement for VRChat PhysBones (whose simulation is editor-only
    /// here). Parameters mirror PhysBones (pull/stiffness/spring/gravity) and the editor
    /// step imports each chain's real values so the feel matches VRChat.
    ///
    /// Key to avoiding droop: every frame the chain is snapped back to its rest pose to
    /// read clean rest targets, THEN simulated. That stops sag from compounding down the
    /// chain (the bug in the first version).
    /// </summary>
    // After head/eye/idle so hair reacts to their motion.
    [DefaultExecutionOrder(200)]
    public class SpringBone : MonoBehaviour
    {
        public Transform root;

        [Tooltip("Velocity lost per 60th-second (higher = settles faster).")]
        [Range(0f, 1f)] public float damping = 0.25f;
        [Tooltip("Pull back toward rest each frame (PhysBone 'pull').")]
        [Range(0f, 1f)] public float elasticity = 0.2f;
        [Tooltip("Max deviation from rest, as a fraction of bone length (PhysBone 'stiffness').")]
        [Range(0f, 1f)] public float stiffness = 0.2f;
        [Tooltip("Downward acceleration in m/s^2 (PhysBone 'gravity' * g). Hair is often ~0.")]
        public float gravity = 0f;
        [Range(1, 4)] public int iterations = 2;
        public bool simulate = true;

        // Imported base values (from the PhysBone). AppController scales these by global
        // multipliers so you can nudge all chains at once without losing per-chain ratios.
        [HideInInspector] public float baseDamping = 0.25f, baseElasticity = 0.2f,
                                       baseStiffness = 0.2f, baseGravity = 0f;

        private class Node
        {
            public Transform t;
            public int parent;
            public int singleChild;
            public bool pinned;
            // Sim / sim-prev / rest world pos this frame.
            public Vector3 pos, prev, rigid;
            public Vector3 restLocalPos;
            public Quaternion restLocalRot;
            public float boneLen;
        }

        private readonly List<Node> _n = new List<Node>();
        private bool _ready;

        private void Start() => Setup();

        public void Setup()
        {
            _n.Clear();
            if (root == null) root = transform;
            Build(root, -1);

            var cc = new int[_n.Count];
            for (int i = 0; i < _n.Count; i++)
                if (_n[i].parent >= 0) cc[_n[i].parent]++;
            for (int i = 0; i < _n.Count; i++)
            {
                _n[i].singleChild = -1;
                _n[i].pinned = _n[i].parent < 0 || cc[_n[i].parent] > 1;
            }
            for (int i = 0; i < _n.Count; i++)
            {
                int p = _n[i].parent;
                if (p >= 0 && cc[p] == 1) _n[p].singleChild = i;
            }
            _ready = _n.Count > 1;
        }

        private void Build(Transform t, int parentIdx)
        {
            int idx = _n.Count;
            _n.Add(new Node
            {
                t = t,
                parent = parentIdx,
                pos = t.position,
                prev = t.position,
                restLocalPos = t.localPosition,
                restLocalRot = t.localRotation,
                boneLen = parentIdx >= 0 ? Vector3.Distance(t.position, _n[parentIdx].t.position) : 0f,
            });
            for (int i = 0; i < t.childCount; i++)
                Build(t.GetChild(i), idx);
        }

        private void LateUpdate()
        {
            if (!simulate || !_ready) return;
            // Clamp so hitches don't explode it.
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f) return;

            // Normalise feel to a 60 fps baseline.
            float step = dt * 60f;
            float dampF = Mathf.Pow(Mathf.Clamp01(1f - damping), step);
            float elasF = 1f - Mathf.Pow(Mathf.Clamp01(1f - elasticity), step);
            Vector3 grav = Vector3.down * gravity * dt * dt;

            // 1) Snap the chain back to its rest pose so we read CLEAN rest targets.
            //    Includes the root, which itself swings on single-strand chains and would
            //    otherwise accumulate rotation frame over frame.
            for (int i = 0; i < _n.Count; i++) _n[i].t.localRotation = _n[i].restLocalRot;
            // 2) Capture rest world positions (whole chain rigid, following head/body).
            for (int i = 0; i < _n.Count; i++) _n[i].rigid = _n[i].t.position;
            _n[0].pos = _n[0].rigid;
            _n[0].prev = _n[0].rigid;

            // 3) Verlet integrate free particles.
            for (int i = 1; i < _n.Count; i++)
            {
                var p = _n[i];
                if (p.pinned) continue;
                Vector3 v = (p.pos - p.prev) * dampF;
                p.prev = p.pos;
                p.pos += v + grav;
                if (!Finite(p.pos)) { p.pos = p.rigid; p.prev = p.rigid; }
            }

            // 4) Constraints: elasticity pulls toward rest, stiffness clamps how far it
            //    can deviate, length keeps bones rigid. Several passes = firmer.
            for (int pass = 0; pass < Mathf.Max(1, iterations); pass++)
            {
                for (int i = 1; i < _n.Count; i++)
                {
                    var p = _n[i];
                    var par = _n[p.parent];
                    if (p.pinned) { p.pos = p.rigid; p.prev = p.rigid; continue; }

                    p.pos = Vector3.Lerp(p.pos, p.rigid, elasF);

                    Vector3 dev = p.pos - p.rigid;
                    float dl = dev.magnitude;
                    float maxDev = p.boneLen * (1f - stiffness);
                    if (dl > maxDev && dl > 1e-6f) p.pos = p.rigid + dev * (maxDev / dl);

                    Vector3 d = p.pos - par.pos;
                    float l = d.magnitude;
                    if (l > 1e-6f) p.pos = par.pos + d * (p.boneLen / l);
                }
            }

            // 5) Apply: a bone with one child aims at the child's simulated position.
            for (int i = 0; i < _n.Count; i++)
            {
                var par = _n[i];
                if (par.singleChild < 0) continue;
                var ch = _n[par.singleChild];
                Vector3 ori = par.t.TransformPoint(ch.restLocalPos) - par.t.position;
                Vector3 cur = ch.pos - par.pos;
                if (ori.sqrMagnitude > 1e-10f && cur.sqrMagnitude > 1e-10f)
                    par.t.rotation = Quaternion.FromToRotation(ori, cur) * par.t.rotation;
            }
        }

        private static bool Finite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsInfinity(v.x) ||
              float.IsNaN(v.y) || float.IsInfinity(v.y) ||
              float.IsNaN(v.z) || float.IsInfinity(v.z));
    }
}
