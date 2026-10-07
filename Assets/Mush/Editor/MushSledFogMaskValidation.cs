using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>GPU checks for selective fog masking, occlusion and stencil clearing.</summary>
public static class MushSledFogMaskValidation
{
    [MenuItem("Mush/Rendering/Validate Sled Fog Mask")]
    public static void Validate()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Validate in Edit Mode.");
        Scene preview = default;
        RenderPipelineAsset quality = QualitySettings.renderPipeline;
        RenderPipelineAsset defaults = GraphicsSettings.defaultRenderPipeline;
        RenderTexture previous = RenderTexture.active;
        var temporary = new List<UnityEngine.Object>();
        Camera camera = null;
        try
        {
            MushSledFogMaskInstaller.ValidateBindings(false);
            // Keep the user's open scenes and unsaved edits intact.
            preview = EditorSceneManager.NewPreviewScene();
            camera = new GameObject("Fog mask test camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, preview);
            camera.scene = preview;
            camera.transform.position = new Vector3(0, 0, -6);
            camera.orthographic = true;
            camera.orthographicSize = 2;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            UniversalAdditionalCameraData extra = camera.GetUniversalAdditionalCameraData();
            extra.renderPostProcessing = false;
            extra.renderShadows = false;
            extra.allowXRRendering = false;
            extra.SetRenderer(0);
            RenderTexture target = new(256, 256, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            target.Create();
            temporary.Add(target);
            camera.targetTexture = target;
            Texture2D pixels = new(256, 256, TextureFormat.RGB24, false);
            temporary.Add(pixels);

            Material red = Unlit(Color.red, temporary);
            Material green = Unlit(Color.green, temporary);
            Material yellow = Unlit(Color.yellow, temporary);
            GameObject sled = Cube("Protected sled", new Vector3(-0.65f, 0, 0), red, preview);
            Require(MushSledFogMask.ApplyToSled(sled.transform) == 1, "The runtime sled assignment failed.");
            Cube("Ordinary object", new Vector3(0.65f, 0, 0), green, preview);
            GameObject occluder = Cube("Opaque wall", new Vector3(-0.65f, 0, -0.65f), yellow, preview);
            occluder.SetActive(false);
            GameObject fog = GameObject.CreatePrimitive(PrimitiveType.Quad);
            SceneManager.MoveGameObjectToScene(fog, preview);
            fog.name = "Selected fog";
            fog.transform.position = new Vector3(0, 0, -1.5f);
            fog.transform.localScale = new Vector3(4, 4, 1);
            Mesh mesh = UnityEngine.Object.Instantiate(fog.GetComponent<MeshFilter>().sharedMesh);
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            temporary.Add(mesh);
            fog.GetComponent<MeshFilter>().sharedMesh = mesh;
            Material protectedFog = new(AssetDatabase.LoadAssetAtPath<Material>(MushSledFogMaskInstaller.FogMaterialPath));
            protectedFog.SetTexture("_BaseMap", Texture2D.whiteTexture);
            protectedFog.SetColor("_BaseColor", Color.blue);
            protectedFog.SetFloat("_Cull", 0);
            temporary.Add(protectedFog);
            Material ordinaryFog = new(protectedFog) { shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") };
            temporary.Add(ordinaryFog);
            MeshRenderer fogRenderer = fog.GetComponent<MeshRenderer>();
            GameObject particleFog = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(MushSledFogMaskInstaller.FogPrefabPath));
            SceneManager.MoveGameObjectToScene(particleFog, preview);
            particleFog.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            ParticleSystem particles = particleFog.GetComponentInChildren<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0;
            main.startSize3D = false;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = false;
            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = false;
            ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = protectedFog;
            particleRenderer.pivot = Vector3.zero;
            particleRenderer.maxParticleSize = 1;
            particleFog.SetActive(false);

            foreach (string preset in new[] { "Quality", "Performance" })
            {
                UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                    "Assets/VR/Settings/Project Configuration/" + preset + " URP Config.asset");
                Require(pipeline != null, "Missing pipeline: " + preset);
                QualitySettings.renderPipeline = pipeline;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                sled.transform.position = new Vector3(-0.65f, 0, 0);
                fogRenderer.sharedMaterial = protectedFog;
                occluder.SetActive(false);
                Capture(camera, pixels, preset + "-protected");
                Expect(pixels, camera, sled.transform.position, Color.red, preset + ": selected fog hides ordinary objects but not sled");
                Expect(pixels, camera, new Vector3(0.65f, 0, 0), Color.blue, preset + ": ordinary objects retain fog");
                Expect(pixels, camera, new Vector3(0, 1.3f, 0), Color.blue, preset + ": background retains fog");

                fogRenderer.sharedMaterial = ordinaryFog;
                Capture(camera, pixels, preset + "-other-vfx");
                Expect(pixels, camera, sled.transform.position, Color.blue, preset + ": other VFX retain normal rendering over sled");

                fogRenderer.sharedMaterial = protectedFog;
                occluder.SetActive(true);
                Capture(camera, pixels, preset + "-occluded");
                Expect(pixels, camera, sled.transform.position, Color.blue, preset + ": hidden sled does not write through an opaque wall");

                occluder.SetActive(false);
                sled.transform.position = new Vector3(-1.3f, 0, 0);
                Capture(camera, pixels, preset + "-moved");
                Expect(pixels, camera, sled.transform.position, Color.red, preset + ": mask follows sled movement");
                Expect(pixels, camera, new Vector3(-0.65f, 0, 0), Color.blue, preset + ": old stencil pixels are cleared each frame");

                fog.SetActive(false);
                particleFog.SetActive(true);
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.Play(true);
                particles.Emit(new ParticleSystem.EmitParams
                {
                    position = new Vector3(0, 0, -1.5f), startColor = Color.white,
                    startSize = 4, startLifetime = 10, velocity = Vector3.zero
                }, 1);
                particles.Simulate(0.01f, true, false, true);
                Require(particles.particleCount == 1, "The particle fixture must emit one visible particle.");
                // Force particle geometry generation when rendering a preview scene in Edit Mode.
                Mesh particleMesh = new();
                particles.GetComponent<ParticleSystemRenderer>().BakeMesh(particleMesh, camera, true);
                temporary.Add(particleMesh);
                Require(particleMesh.vertexCount > 0, "The particle fixture did not produce billboard geometry.");
                Capture(camera, pixels, preset + "-particle-prefab");
                Expect(pixels, camera, sled.transform.position, Color.red, preset + ": real fog particle preserves sled");
                Expect(pixels, camera, new Vector3(0.65f, 0, 0), Color.blue, preset + ": real fog particle remains over ordinary geometry");
                particleFog.SetActive(false);
                fog.SetActive(true);
            }
            Require(!ShaderUtil.ShaderHasError(protectedFog.shader), "The protected fog shader failed GPU compilation.");
            Debug.Log("[Mush Fog Mask GPU] PASS: PC/Android pipeline presets, selective filtering, opaque occlusion and moving-mask clearing (4x MSAA).");
        }
        finally
        {
            RenderTexture.active = previous;
            QualitySettings.renderPipeline = quality;
            GraphicsSettings.defaultRenderPipeline = defaults;
            if (camera != null) camera.targetTexture = null;
            foreach (UnityEngine.Object item in temporary) UnityEngine.Object.DestroyImmediate(item);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static Material Unlit(Color color, List<UnityEngine.Object> temporary)
    {
        Material material = new(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", color);
        temporary.Add(material);
        return material;
    }

    private static GameObject Cube(string name, Vector3 position, Material material, Scene scene)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(cube, scene);
        cube.name = name;
        cube.transform.position = position;
        cube.transform.localScale = Vector3.one * 0.8f;
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        return cube;
    }

    private static void Capture(Camera camera, Texture2D pixels, string name)
    {
        camera.Render();
        RenderTexture.active = camera.targetTexture;
        pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
        pixels.Apply();
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../CodexOutput/MushFogMaskQA"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
    }

    private static void Expect(Texture2D pixels, Camera camera, Vector3 position, Color expected, string message)
    {
        Vector3 viewport = camera.WorldToViewportPoint(position);
        Color actual = pixels.GetPixel(Mathf.RoundToInt(viewport.x * 255), Mathf.RoundToInt(viewport.y * 255));
        Require(Mathf.Abs(actual.r - expected.r) < 0.15f && Mathf.Abs(actual.g - expected.g) < 0.15f &&
                Mathf.Abs(actual.b - expected.b) < 0.15f, message + "; actual=" + actual + ", expected=" + expected);
        Debug.Log("[Mush Fog Mask GPU] " + message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
