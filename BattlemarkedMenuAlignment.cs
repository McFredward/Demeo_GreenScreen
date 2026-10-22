#nullable disable
#if DEMEOBATTLEMARKED
using System;
using System.Reflection;
using UnityEngine;

namespace GreenScreen
{
    // One local rigid transform after the game's menu waypoint placement.
    // No tracking-origin reset, update loop or gameplay/manual placement hook.
    internal static class BattlemarkedMenuAlignment
    {
        private static bool _warned;
        internal static void Align(object avatar, Vector3 position, Vector3 rotation)
        {
            try
            {
                Transform head = Read(avatar, "HeadTransform"), root = Read(avatar, "HeadRootTransform"), hands = Read(avatar, "HandRootTransform");
                if (head == null || root == null || hands == null || head == root || !head.IsChildOf(root)) return;
                Vector3 pivot = head.position, forward = head.forward;
                Vector3 target = Quaternion.Euler(rotation) * Vector3.forward;
                if (!Finite(position) || !Finite(rotation) || !Finite(pivot) || !Finite(forward)
                    || forward.x * forward.x + forward.z * forward.z < .0001f
                    || target.x * target.x + target.z * target.z < .0001f) return;
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
                if (!Finite(firstPosition) || second != null && !Finite(secondPosition)) return;
                first.SetPositionAndRotation(firstPosition, firstRotation);
                if (second != null) second.SetPositionAndRotation(secondPosition, secondRotation);
                Utils.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Battlemarked menu aligned once to current head pose: yaw correction={0:F2}; horizontal offset=({1:F3},{2:F3}); at={3}. Tracking origin, height and avatar scale retained.",
                    yaw, offset.x, offset.z, DateTime.Now.ToString("HH:mm:ss.fff")));
            }
            catch (Exception error)
            {
                if (!_warned) Utils.LogWarning("Battlemarked menu alignment unavailable; retaining game placement: " + error.GetType().Name + ": " + error.Message);
                _warned = true;
            }
        }
        private static Transform Read(object avatar, string name) => avatar.GetType()
            .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(avatar) as Transform;
        private static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x)
            || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));
    }
}
#endif
