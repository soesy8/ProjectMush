using UnityEngine;
using UnityEngine.Rendering;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyFireplaceVfx : MonoBehaviour
    {
        private const string FireplaceRootName = "PROP_FireplaceRoot";
        private const string FlameObjectName = "Mush Fireplace Flame VFX";

        private Material flameMaterial;
        private Texture2D flameTexture;
        private Light fireplaceLight;
        private float baseLightIntensity;
        private float baseLightRange;
        private float flickerSeed;

        public static MushLobbyFireplaceVfx Install(Transform lobbyRoot)
        {
            if (lobbyRoot == null)
                return null;

            Transform fireplace = FindDescendant(lobbyRoot, FireplaceRootName);
            if (fireplace == null)
                return null;

            // 이 FBX는 벽난로의 높이가 로컬 Z축으로 저장되어 있다. 루트의
            // -90도 X 회전으로 Z축을 월드 위쪽으로 바꿔 기둥과 상단을 세운다.
            fireplace.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            MushLobbyFireplaceVfx existing = fireplace.GetComponent<MushLobbyFireplaceVfx>();
            if (existing == null)
                existing = fireplace.gameObject.AddComponent<MushLobbyFireplaceVfx>();
            existing.Initialize(lobbyRoot);
            return existing;
        }

        private void Initialize(Transform lobbyRoot)
        {
            if (transform.Find(FlameObjectName) == null)
                CreateFlameParticles();

            fireplaceLight = FindFireplaceLight();
            if (fireplaceLight != null)
            {
                baseLightIntensity = fireplaceLight.intensity;
                baseLightRange = fireplaceLight.range;
            }
            flickerSeed = Random.Range(0f, 100f);
        }

        private void CreateFlameParticles()
        {
            Transform modelFlame = FindDescendant(transform, "PROP_FireFlame");
            Vector3 flameLocalPosition = modelFlame != null
                ? modelFlame.localPosition + new Vector3(0f, 0f, 0.03f)
                : new Vector3(0f, -0.25f, 0.49f);

            GameObject effectObject = new(FlameObjectName);
            effectObject.transform.SetParent(transform, false);
            effectObject.transform.localPosition = flameLocalPosition;
            effectObject.transform.localRotation = Quaternion.identity;

            ParticleSystem particles = effectObject.AddComponent<ParticleSystem>();
            // AddComponent 직후 기본 ParticleSystem이 이미 재생 상태일 수 있다.
            // duration 같은 재생 중 변경 불가 값을 만지기 전에 입자까지 완전히 비운다.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Clear(true);
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 36;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.78f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.22f, 0.52f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.82f, 0.20f, 0.92f),
                new Color(1f, 0.20f, 0.025f, 0.78f));

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 24f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 13f;
            shape.radius = 0.11f;
            shape.length = 0.12f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient flameGradient = new();
            flameGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.92f, 0.35f), 0f),
                    new GradientColorKey(new Color(1f, 0.32f, 0.035f), 0.55f),
                    new GradientColorKey(new Color(0.55f, 0.04f, 0.01f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.95f, 0.12f),
                    new GradientAlphaKey(0.72f, 0.62f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = flameGradient;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.25f),
                    new Keyframe(0.18f, 1f),
                    new Keyframe(1f, 0.10f)));

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 0.065f;
            noise.frequency = 2.2f;
            noise.scrollSpeed = 0.35f;

            ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sortMode = ParticleSystemSortMode.YoungestInFront;
            flameMaterial = CreateFlameMaterial(out flameTexture);
            if (flameMaterial != null)
                particleRenderer.sharedMaterial = flameMaterial;

            particles.Play(true);
        }

        private void Update()
        {
            if (fireplaceLight == null)
                return;

            float noise = Mathf.PerlinNoise(flickerSeed, Time.unscaledTime * 7.5f);
            fireplaceLight.intensity = baseLightIntensity * Mathf.Lerp(0.78f, 1.12f, noise);
            fireplaceLight.range = baseLightRange * Mathf.Lerp(0.94f, 1.04f, noise);
        }

        private static Material CreateFlameMaterial(out Texture2D texture)
        {
            texture = CreateSoftParticleTexture();
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                            Shader.Find("Particles/Standard Unlit") ??
                            Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return null;

            Material material = new(shader) { name = "Mush Fireplace Flame Material" };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.One);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static Texture2D CreateSoftParticleTexture()
        {
            const int size = 32;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                name = "Mush Fireplace Soft Flame",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 uv = new((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                    uv.x *= 1.20f;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - uv.magnitude), 1.8f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root == null)
                return null;
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == objectName)
                    return candidate;
            }
            return null;
        }

        private static Light FindFireplaceLight()
        {
            foreach (Light candidate in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate != null && candidate.name == "Fireplace Light")
                    return candidate;
            }
            return null;
        }

        private void OnDestroy()
        {
            if (flameMaterial != null)
                Destroy(flameMaterial);
            if (flameTexture != null)
                Destroy(flameTexture);
        }
    }
}
