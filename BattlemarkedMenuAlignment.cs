#nullable disable
#if DEMEOBATTLEMARKED
using System;
using System.Reflection;
using UnityEngine;

namespace GreenScreen
{
    // The game resets its logical head during waypoint placement. Wait for the
    // current stereo view matrices after XR pose update, then stop.
    // Never reset the runtime origin or realign gameplay/manual placements.
    internal static class BattlemarkedMenuAlignment
    {
        private static bool _warned;
        private static object _pending;
        private static Vector3 _position, _rotation;
        private static int _frame;
        private static float _deadline;
        internal static void Request(object avatar, Vector3 position, Vector3 rotation)
        {
            _pending = avatar; _position = position; _rotation = rotation;
            _frame = Time.frameCount; _deadline = Time.unscaledTime + 2;
            Utils.LogInfo("Battlemarked menu alignment queued for the next admitted stereo render pose.");
        }
        internal static void Cancel() => _pending = null;
        internal static void Tick()
        {
            if (_pending == null) return;
            if (!ConfigurationStore.Current.SteamLinkNativePassthrough || !SteamLinkConnection.Available
                || !SteamLinkConnectionLifecycle.UsesOpenXr)
            { Cancel(); return; }
            if (Time.unscaledTime > _deadline)
            {
                if (!_warned) Utils.LogWarning("Battlemarked menu alignment expired without an admitted render camera under the avatar head root; no delayed recenter will be applied.");
                _warned = true; Cancel(); return;
            }
        }
        internal static void BeforeRender(Camera camera)
        {
            Tick();
            if (_pending == null || Time.frameCount <= _frame || camera == null || camera.transform == null) return;
            try
            {
                // Battlemarked can reset the logical Camera transform while XR
                // already supplies a different late tracked pose to rendering.
                // Use both current eye views; never guess a tracking-space parent
                // or consume the reset transform as a successfully aligned head.
                Matrix4x4 left = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
                Matrix4x4 right = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
                if (Math.Abs(left.determinant) < .000001f || Math.Abs(right.determinant) < .000001f) return;
                left = left.inverse; right = right.inverse;
                Vector3 a = left.MultiplyPoint3x4(Vector3.zero), b = right.MultiplyPoint3x4(Vector3.zero);
                Vector3 forwardA = left.MultiplyVector(Vector3.back), forwardB = right.MultiplyVector(Vector3.back);
                if (!Finite(a) || !Finite(b) || (a - b).sqrMagnitude < .00000001f
                    || !Finite(forwardA) || !Finite(forwardB)
                    || Vector3.Dot(forwardA.normalized, forwardB.normalized) < .95f) return;
                if (Align(_pending, camera.transform, _position, _rotation, (a + b) * .5f, forwardA + forwardB)) Cancel();
            }
            catch (Exception error)
            {
                if (!_warned) Utils.LogWarning("Battlemarked stereo menu pose unavailable; retaining game placement: " + error.GetType().Name + ": " + error.Message);
                _warned = true; Cancel();
            }
        }
        internal static bool Align(object avatar, Transform head, Vector3 position, Vector3 rotation)
            => head != null && Align(avatar, head, position, rotation, head.position, head.forward);
        private static bool Align(object avatar, Transform head, Vector3 position, Vector3 rotation, Vector3 pivot, Vector3 forward)
        {
            try
            {
                Transform root = Read(avatar, "HeadRootTransform"), hands = Read(avatar, "HandRootTransform");
                if (head == null || root == null || hands == null || head == root || !head.IsChildOf(root)) return false;
                Vector3 target = Quaternion.Euler(rotation) * Vector3.forward;
                if (!Finite(position) || !Finite(rotation) || !Finite(pivot) || !Finite(forward)
                    || forward.x * forward.x + forward.z * forward.z < .0001f
                    || target.x * target.x + target.z * target.z < .0001f) return false;
                float yaw = (float)((Math.Atan2(target.x, target.z) - Math.Atan2(forward.x, forward.z)) * 180 / Math.PI);
                Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
                Vector3 offset = new Vector3(position.x - pivot.x, 0, position.z - pivot.z);
                // Roots may be identical, nested, or separate in the rig. Apply
                // the same world delta exactly once to both head and hands.
                Transform first = root.IsChildOf(hands) ? hands : root;
                Transform second = first == root && hands != root && !hands.IsChildOf(root) ? hands : null;
                Vector3 firstPosition = pivot + turn * (first.position - pivot) + offset;
                Quaternion firstRotation = turn * first.rotation;
                Vector3 secondPosition = second == null ? default : pivot + turn * (second.position - pivot) + offset;
                Quaternion secondRotation = second == null ? default : turn * second.rotation;
                if (!Finite(firstPosition) || second != null && !Finite(secondPosition)) return false;
                first.SetPositionAndRotation(firstPosition, firstRotation);
                if (second != null) second.SetPositionAndRotation(secondPosition, secondRotation);
                Utils.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Battlemarked menu aligned once to current stereo head pose: render camera={4}; yaw correction={0:F2}; horizontal offset=({1:F3},{2:F3}); at={3}. Tracking origin, height and avatar scale retained.",
                    yaw, offset.x, offset.z, DateTime.Now.ToString("HH:mm:ss.fff"), head.name));
                return true;
            }
            catch (Exception error)
            {
                if (!_warned) Utils.LogWarning("Battlemarked menu alignment unavailable; retaining game placement: " + error.GetType().Name + ": " + error.Message);
                _warned = true;
                Cancel(); return false;
            }
        }
        private static Transform Read(object avatar, string name) => avatar.GetType()
            .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(avatar) as Transform;
        private static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x)
            || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));
    }
}
#endif
