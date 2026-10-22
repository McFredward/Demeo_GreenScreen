#nullable disable
#if DEMEOBATTLEMARKED
using System;
using System.Reflection;
using UnityEngine;

namespace GreenScreen
{
    // The game resets its logical head during waypoint placement. Wait for the
    // next camera update and use the actual XR render camera, then stop.
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
            Utils.LogInfo("Battlemarked menu alignment queued for the next tracked camera LateUpdate.");
        }
        internal static void Cancel() => _pending = null;
        internal static void LateTick(Camera camera)
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
            if (Time.frameCount <= _frame || camera == null || camera.transform == null) return;
            object avatar = _pending;
            if (Align(avatar, camera.transform, _position, _rotation)) Cancel();
        }
        internal static bool Align(object avatar, Transform head, Vector3 position, Vector3 rotation)
        {
            try
            {
                Transform root = Read(avatar, "HeadRootTransform"), hands = Read(avatar, "HandRootTransform");
                if (head == null || root == null || hands == null || head == root || !head.IsChildOf(root)) return false;
                Vector3 pivot = head.position, forward = head.forward;
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
                    "Battlemarked menu aligned once to current head pose: render camera={4}; yaw correction={0:F2}; horizontal offset=({1:F3},{2:F3}); at={3}. Tracking origin, height and avatar scale retained.",
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
