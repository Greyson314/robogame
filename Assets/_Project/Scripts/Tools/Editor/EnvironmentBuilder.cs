using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Drops the static decor (ground, walls, props, lighting, camera clear
    /// colour) for each named scene. Garage and Arena get visually distinct
    /// rigs so the player has an obvious sense of "where am I".
    /// </summary>
    /// <remarks>
    /// Idempotent: each call destroys the previous "Environment" parent
    /// before rebuilding, so re-running never duplicates geometry.
    /// </remarks>
    internal static class EnvironmentBuilder
    {
        private const string EnvRoot = "Environment";

        // -----------------------------------------------------------------
        // Garage: small enclosed bay with a turntable podium for the chassis.
        // -----------------------------------------------------------------

        public static void BuildGarageEnvironment()
        {
            EnsureCameraAndLight(WorldPalette.GarageClear, lightEuler: new Vector3(45f, 30f, 0f));

            GameObject env = ResetEnvRoot();

            // Floor — small, dark.
            const float halfBay = 9f;
            GameObject floor = MakePlane(env.transform, "Floor", Vector3.zero, scale: halfBay / 5f);
            WorldPalette.Apply(floor, WorldPalette.GarageFloor);

            // Walls — short box ring, no roof so the camera can swing around.
            const float wallH = 4f, wallT = 0.5f;
            MakeWall(env.transform, "Wall_N", new Vector3(0f, wallH * 0.5f,  halfBay), new Vector3(halfBay * 2f, wallH, wallT));
            MakeWall(env.transform, "Wall_S", new Vector3(0f, wallH * 0.5f, -halfBay), new Vector3(halfBay * 2f, wallH, wallT));
            MakeWall(env.transform, "Wall_E", new Vector3( halfBay, wallH * 0.5f, 0f), new Vector3(wallT, wallH, halfBay * 2f));
            MakeWall(env.transform, "Wall_W", new Vector3(-halfBay, wallH * 0.5f, 0f), new Vector3(wallT, wallH, halfBay * 2f));

            // Hazard stripes — flat boxes flush with the floor, framing the spawn pad.
            const float padHalf = 3f, stripeT = 0.04f, stripeW = 0.4f;
            MakeAccent(env.transform, "Stripe_N", new Vector3(0f, stripeT, padHalf),  new Vector3(padHalf * 2f, stripeT, stripeW));
            MakeAccent(env.transform, "Stripe_S", new Vector3(0f, stripeT, -padHalf), new Vector3(padHalf * 2f, stripeT, stripeW));
            MakeAccent(env.transform, "Stripe_E", new Vector3( padHalf, stripeT, 0f), new Vector3(stripeW, stripeT, padHalf * 2f));
            MakeAccent(env.transform, "Stripe_W", new Vector3(-padHalf, stripeT, 0f), new Vector3(stripeW, stripeT, padHalf * 2f));

            // Podium — short cylinder beneath the spawn point so the chassis
            // looks staged rather than dropped.
            GameObject podium = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            podium.name = "Podium";
            podium.transform.SetParent(env.transform, worldPositionStays: false);
            podium.transform.position = new Vector3(0f, 0.05f, 0f);
            podium.transform.localScale = new Vector3(2.4f, 0.05f, 2.4f);
            WorldPalette.Apply(podium, WorldPalette.GaragePodium);
        }

        // -----------------------------------------------------------------
        // Arena: large open field + the obstacle course.
        // -----------------------------------------------------------------

        public static void BuildArenaEnvironment()
        {
            EnsureCameraAndLight(WorldPalette.ArenaClear, lightEuler: new Vector3(50f, -30f, 0f));

            // Ground: scale 22 plane → 220m. Coloured grass-green.
            GameObject ground = ScaffoldUtils.GetOrCreate(
                "Ground",
                () => GameObject.CreatePrimitive(PrimitiveType.Plane));
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(22f, 1f, 22f);
            WorldPalette.Apply(ground, WorldPalette.ArenaGround);

            // Re-use the existing obstacle course populator, then tint it.
            SceneScaffolder.PopulateTestTerrain();
            TintTerrain();
        }

        private static void TintTerrain()
        {
            GameObject terrain = GameObject.Find("Terrain");
            if (terrain == null) return;

            foreach (Transform child in terrain.transform)
            {
                string n = child.name;
                Material mat = WorldPalette.ArenaWall; // default fallback
                if (n.StartsWith("Ramp_")) mat = WorldPalette.ArenaRamp;
                else if (n.StartsWith("Bump_")) mat = WorldPalette.ArenaBump;
                else if (n.StartsWith("Stair_")) mat = WorldPalette.ArenaStair;
                else if (n.StartsWith("Pillar_")) mat = WorldPalette.ArenaPillar;
                else if (n.StartsWith("Wall_")) mat = WorldPalette.ArenaWall;
                WorldPalette.Apply(child.gameObject, mat);
            }
        }

        // -----------------------------------------------------------------
        // Shared helpers
        // -----------------------------------------------------------------

        private static GameObject ResetEnvRoot()
        {
            GameObject existing = GameObject.Find(EnvRoot);
            if (existing != null) Object.DestroyImmediate(existing);

            // Also remove the old plain Ground from any earlier garage rig
            // and the obstacle course in case we're re-skinning a scene
            // that previously called PopulateTestTerrain().
            GameObject oldGround = GameObject.Find("Ground");
            if (oldGround != null) Object.DestroyImmediate(oldGround);
            GameObject oldTerrain = GameObject.Find("Terrain");
            if (oldTerrain != null) Object.DestroyImmediate(oldTerrain);

            return new GameObject(EnvRoot);
        }

        private static GameObject MakePlane(Transform parent, string name, Vector3 pos, float scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(scale, 1f, scale);
            return go;
        }

        private static void MakeWall(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.localScale = size;
            WorldPalette.Apply(go, WorldPalette.GarageWall);
        }

        private static void MakeAccent(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.localScale = size;
            // Strip collider — these are decorative stripes.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            WorldPalette.Apply(go, WorldPalette.GarageAccent);
        }

        private static void EnsureCameraAndLight(Color clearColor, Vector3 lightEuler)
        {
            GameObject camGO = GameObject.Find("Main Camera");
            if (camGO == null)
            {
                camGO = new GameObject("Main Camera");
                camGO.AddComponent<Camera>();
                camGO.AddComponent<AudioListener>();
                camGO.tag = "MainCamera";
            }
            camGO.transform.position = new Vector3(0f, 8f, -10f);
            camGO.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            Camera cam = camGO.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clearColor;

            GameObject light = ScaffoldUtils.GetOrCreate("Directional Light");
            var lightComp = light.GetComponent<Light>();
            if (lightComp == null) lightComp = light.AddComponent<Light>();
            lightComp.type = LightType.Directional;
            lightComp.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(lightEuler);
        }
    }
}
