#nullable disable
#if DEMEOBATTLEMARKED
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
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
                if (!ReadStereoPose(camera, out Vector3 a, out Vector3 b, out Vector3 forwardA, out Vector3 forwardB)
                    || (a - b).sqrMagnitude < .00000001f
                    || Vector3.Dot(forwardA.normalized, forwardB.normalized) < .95f) return;
                if (Align(_pending, camera.transform, _position, _rotation, (a + b) * .5f, forwardA + forwardB)) Cancel();
            }
            catch (Exception error)
            {
                if (!_warned) Utils.LogWarning("Battlemarked stereo menu pose unavailable; retaining game placement: " + error.GetType().Name + ": " + error.Message);
                _warned = true; Cancel();
            }
        }
        // Unity6's generated Matrix4x4 wrappers can contain invalid managed IL.
        // Call the checked injected ABI with an owned blittable matrix and do
        // the affine inverse locally; no generated struct-return/math call runs.
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeView
        {
            public float m00,m10,m20,m30,m01,m11,m21,m31,m02,m12,m22,m32,m03,m13,m23,m33;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GetView(IntPtr self, Camera.StereoscopicEye eye, out NativeView view);
        private static GetView _view;
        private static bool ReadStereoPose(Camera camera, out Vector3 a, out Vector3 b, out Vector3 forwardA, out Vector3 forwardB)
        {
            a=b=forwardA=forwardB=default;
            if (IntPtr.Size != 8 || !Application.unityVersion.StartsWith("6000.", StringComparison.Ordinal))
                throw new NotSupportedException("Unknown Unity stereo view ABI.");
            if (_view == null)
            {
                const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
                var method=typeof(Camera).GetMethod("GetStereoViewMatrix_Injected",flags,null,
                    new[]{typeof(IntPtr),typeof(Camera.StereoscopicEye),typeof(Matrix4x4).MakeByRefType()},null);
                if (method?.ReturnType != typeof(void) || Marshal.SizeOf(typeof(Matrix4x4)) != 64
                    || Marshal.SizeOf(typeof(NativeView)) != 64
                    || Marshal.OffsetOf(typeof(Matrix4x4),"m03").ToInt32() != 48)
                    throw new NotSupportedException("Unknown Unity stereo matrix layout.");
                _view=IL2CPP.ResolveICall<GetView>("UnityEngine.Camera::GetStereoViewMatrix_Injected");
            }
            IntPtr self=camera.m_CachedPtr;
            if (self == IntPtr.Zero) return false;
            _view(self,Camera.StereoscopicEye.Left,out NativeView left);
            _view(self,Camera.StereoscopicEye.Right,out NativeView right);
            GC.KeepAlive(camera);
            return Pose(left,out a,out forwardA) && Pose(right,out b,out forwardB);
        }
        private static bool Pose(NativeView v, out Vector3 position, out Vector3 forward)
        {
            position=forward=default;
            if (Math.Abs(v.m30)>0.00001f || Math.Abs(v.m31)>0.00001f || Math.Abs(v.m32)>0.00001f || Math.Abs(v.m33-1)>0.00001f) return false;
            double a=v.m00,b=v.m01,c=v.m02,d=v.m10,e=v.m11,f=v.m12,g=v.m20,h=v.m21,i=v.m22;
            double determinant=a*(e*i-f*h)-b*(d*i-f*g)+c*(d*h-e*g);
            if (double.IsNaN(determinant) || double.IsInfinity(determinant) || Math.Abs(determinant)<1e-12) return false;
            double x0=(e*i-f*h)/determinant,x1=(c*h-b*i)/determinant,x2=(b*f-c*e)/determinant;
            double y0=(f*g-d*i)/determinant,y1=(a*i-c*g)/determinant,y2=(c*d-a*f)/determinant;
            double z0=(d*h-e*g)/determinant,z1=(b*g-a*h)/determinant,z2=(a*e-b*d)/determinant;
            position=new Vector3((float)-(x0*v.m03+x1*v.m13+x2*v.m23),(float)-(y0*v.m03+y1*v.m13+y2*v.m23),(float)-(z0*v.m03+z1*v.m13+z2*v.m23));
            forward=new Vector3((float)-x2,(float)-y2,(float)-z2);
            return Finite(position) && Finite(forward);
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
