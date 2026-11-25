// The free camera: follows the play like the original, but can be turned, tilted and zoomed.
using UnityEngine;

namespace Soccer
{
    public sealed class OrbitCamera : MonoBehaviour
    {
        public Camera Cam;
        public float Yaw = 0f;              // 0 = looking north like the original; positive turns clockwise seen from above
        public float Pitch = 38f;           // degrees below the horizon
        public float Distance = 21f;
        public float Fov = 30f;
        public float ScreenShift = 0.22f;   // moves the action up a little: the status bar covers the bottom of the picture
        public bool AutoOrbit;
        public bool LookAtHouse;            // preset 6: the camera circles the White House instead of the play
        public Vector3 HousePoint;
        public float HouseFront = float.PositiveInfinity;   // Unity z of the residence's south wall (the play camera stays in front of it)
        public bool Tokyo;                  // 东京塔夜场：预设机位换成看塔的，允许仰拍（俯角为负）
        public float[] ParkBox;             // 东京塔夜场：镜头低于 40 m 时不出公园（外面是真的楼和树）：x0, x1, y0, y1（layout 坐标）
        public Vector3 Target;
        public float Shake;                 // set by the effects layer when something big happens; dies away by itself
        float shakeClock;
        Vector3 vel;
        float yawGoal, pitchGoal = 38f, distGoal = 21f, fovGoal = 30f;
        bool started;

        public void Preset(int n)
        {
            AutoOrbit = false; fovGoal = 30f; LookAtHouse = false;
            if (Tokyo && n != 1 && n != 3 && n != 4)
            {
                // 东京塔夜场：2 低机位看塔（塔在球场正后方）、5 反打（身后的王子公园塔）、6 仰拍整座塔（镜头贴着地、往上看）
                switch (n)
                {
                    case 2: yawGoal = 0; pitchGoal = -2; distGoal = 27; fovGoal = 58; break;
                    case 5: yawGoal = 180; pitchGoal = 6; distGoal = 30; fovGoal = 46; break;
                    case 6: yawGoal = 0; pitchGoal = -12; distGoal = 30; fovGoal = 64; break;
                }
                yawGoal = Yaw + Mathf.DeltaAngle(Yaw, yawGoal);
                return;
            }
            switch (n)
            {
                case 1: yawGoal = 0; pitchGoal = 38; distGoal = 21; break;         // the original's view
                case 2: yawGoal = 0; pitchGoal = 5; distGoal = 27; fovGoal = 52; break;         // low, broadcast style: the White House behind the pitch
                case 3: yawGoal = 90; pitchGoal = 22; distGoal = 24; break;        // from behind the west goal
                case 4: yawGoal = 0; pitchGoal = 86; distGoal = 34; break;         // straight down
                case 5: yawGoal = 180; pitchGoal = 8; distGoal = 32; fovGoal = 34; break;       // from the house: the fountain and the Monument
                case 6: yawGoal = 0; pitchGoal = 4; distGoal = 62; fovGoal = 38; LookAtHouse = true; break;      // a visit to the White House: orbit its south front
            }
            // take the short way round
            yawGoal = Yaw + Mathf.DeltaAngle(Yaw, yawGoal);
        }

        public void Snap(float yaw, float pitch, float dist, float fov = 30f, bool house = false)
        {
            LookAtHouse = house;
            Yaw = yawGoal = yaw; Pitch = pitchGoal = pitch; Distance = distGoal = dist; started = false;
            Fov = fovGoal = fov;
        }

        /// <summary>Where the camera should go (it eases there), without the jump of Snap.</summary>
        public void Aim(float yaw, float pitch, float dist, float fov)
        {
            yawGoal = yaw; pitchGoal = pitch; distGoal = dist; fovGoal = fov;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
            {
                yawGoal += Input.GetAxis("Mouse X") * 4.0f;
                pitchGoal -= Input.GetAxis("Mouse Y") * 3.0f;
                AutoOrbit = false;
            }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0) distGoal *= Mathf.Pow(0.9f, wheel);
            if (Keys.Held(KeyCode.Q)) { yawGoal -= 80f * dt; AutoOrbit = false; }
            if (Keys.Held(KeyCode.E)) { yawGoal += 80f * dt; AutoOrbit = false; }
            if (Keys.Held(KeyCode.R)) pitchGoal += 45f * dt;
            if (Keys.Held(KeyCode.F)) pitchGoal -= 45f * dt;
            if (Keys.Held(KeyCode.T) || Keys.Held(KeyCode.Equals)) distGoal *= Mathf.Pow(0.4f, dt);
            if (Keys.Held(KeyCode.G) || Keys.Held(KeyCode.Minus)) distGoal *= Mathf.Pow(2.5f, dt);
            for (int i = 1; i <= 6; i++) if (Keys.Down(KeyCode.Alpha0 + i)) Preset(i);
            if (Keys.Down(KeyCode.C)) AutoOrbit = !AutoOrbit;
            if (AutoOrbit) yawGoal += 10f * dt;
            pitchGoal = Mathf.Clamp(pitchGoal, LookAtHouse ? -6f : (Tokyo ? -16f : 3f), 88f);
            if (LookAtHouse) yawGoal = Mathf.Clamp(yawGoal, -72f, 72f);         // the model has no back
            distGoal = Mathf.Clamp(distGoal, LookAtHouse ? 14f : 5f, LookAtHouse ? 120f : 70f);
        }

        /// <summary>Places the camera.  'focus' is where the action is on the ground.</summary>
        public void Apply(Vector3 focus, float dt, bool snap = false)
        {
            float k = snap ? 1f : 1f - Mathf.Exp(-dt * 10f);
            Yaw = Mathf.Lerp(Yaw, yawGoal, k); Pitch = Mathf.Lerp(Pitch, pitchGoal, k); Distance = Mathf.Lerp(Distance, distGoal, k); Fov = Mathf.Lerp(Fov, fovGoal, k);
            // far out the whole pitch is in view anyway: stop chasing the ball and settle on the middle
            if (LookAtHouse) focus = HousePoint;
            else focus = Vector3.Lerp(focus, Vector3.zero, Mathf.InverseLerp(24f, 42f, Distance));
            if (snap || !started) { Target = focus; vel = Vector3.zero; started = true; }
            else Target = Vector3.SmoothDamp(Target, focus, ref vel, 0.16f, Mathf.Infinity, Mathf.Max(dt, 1e-4f));
            Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
            Cam.transform.rotation = rot;
            Vector3 back = -(rot * Vector3.forward);
            Vector3 pos = Target + back * Distance;
            if (!LookAtHouse && back.z > 1e-3f)
            {
                // Following the play, the camera must not end up inside the house: north of this line stand the stairs
                // and the portico (in the middle) and the wings (further out).  High above the roofs it may pass over.
                float keep = Mathf.Lerp(16.5f, 26f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(45f, 58f, Mathf.Abs(pos.x))));
                float line = HouseFront - keep + Mathf.Max(0f, pos.y - 26f) * 4f;
                if (pos.z > line) pos = Target + back * Mathf.Clamp((line - Target.z) / back.z, 3f, Distance);
            }
            if (Tokyo && ParkBox != null && ParkBox.Length == 4 && pos.y < 40f)
            {
                // 公园外面几十米就是真实的楼（王子公园塔、增上寺）和树林：拉远的镜头停在公园的绿篱以内
                float x0 = ParkBox[0] + 2.5f, x1 = ParkBox[1] - 2.5f, z0 = ParkBox[2] + 2.5f, z1 = ParkBox[3] - 2.5f, t = Distance;
                if (back.x > 1e-4f) t = Mathf.Min(t, (x1 - Target.x) / back.x); else if (back.x < -1e-4f) t = Mathf.Min(t, (x0 - Target.x) / back.x);
                if (back.z > 1e-4f) t = Mathf.Min(t, (z1 - Target.z) / back.z); else if (back.z < -1e-4f) t = Mathf.Min(t, (z0 - Target.z) / back.z);
                pos = Target + back * Mathf.Clamp(t, 3f, Distance);
            }
            if (Shake > 0.001f)
            {
                shakeClock += dt;
                float a = Shake * 0.022f * Mathf.Max(Distance, 8f);
                pos += rot * new Vector3(Mathf.Sin(shakeClock * 71f) + 0.5f * Mathf.Sin(shakeClock * 113f + 1f), Mathf.Sin(shakeClock * 89f + 2f) + 0.5f * Mathf.Sin(shakeClock * 131f), 0f) * a;
                Shake *= Mathf.Exp(-dt * 7.5f);
            }
            else Shake = 0f;
            if (pos.y < 0.7f) pos.y = 0.7f;                                     // never under the lawn
            Cam.transform.position = pos;
            Cam.fieldOfView = Fov; Cam.nearClipPlane = 0.3f; Cam.farClipPlane = 5000f;
            Cam.ResetProjectionMatrix();
            var m = Cam.projectionMatrix; m.m12 -= LookAtHouse ? 0.05f : ScreenShift; Cam.projectionMatrix = m;
        }
    }
}
