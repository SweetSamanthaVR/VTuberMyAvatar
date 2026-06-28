using UnityEngine;

namespace VTuberMyAvatar
{
    public static class AvatarUtil
    {
        /// <summary>
        /// Returns a humanoid Animator. Avatars can carry several Animators (e.g. a
        /// non-humanoid one on a wrapper/child plus the real humanoid rig), so we must
        /// pick the one whose <c>isHuman</c> is true rather than the first found.
        /// Checks the assigned animator, then the hint hierarchy, then the whole scene.
        /// </summary>
        public static Animator FindHumanoid(Animator assigned, GameObject hint)
        {
            if (assigned != null && assigned.isHuman) return assigned;

            if (hint != null)
            {
                foreach (var a in hint.GetComponentsInChildren<Animator>(true))
                    if (a != null && a.isHuman) return a;
            }

            foreach (var a in Object.FindObjectsOfType<Animator>())
                if (a != null && a.isHuman) return a;

            // Last resort (may be null / non-humanoid).
            return assigned;
        }
    }
}
