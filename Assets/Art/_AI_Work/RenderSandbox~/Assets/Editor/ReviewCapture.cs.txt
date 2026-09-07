using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

// Executed only in the isolated RenderSandbox~ project. No auto-run hooks.
public static class ReviewCapture
{
    public static void Run()
    {
        var output = Path.GetFullPath("Captures");
        Directory.CreateDirectory(output);
        var report = new List<string>();
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data,"Assets/ReviewRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(data);
            pipeline.msaaSampleCount=1;
            pipeline.supportsHDR=true;
            AssetDatabase.CreateAsset(pipeline,"Assets/ReviewPipeline.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;
            QualitySettings.renderPipeline=pipeline;
            QualitySettings.shadows=UnityEngine.ShadowQuality.Disable;
            foreach(var mode in new[]{"Baseline","Candidate"})
            foreach(var scene in new[]{"Prop_ShowRoom","PM_Lobby"})
            {
                EditorSceneManager.OpenScene("Assets/"+mode+"/Scenes/"+scene+".unity");
                if(mode=="Candidate" && scene=="PM_Lobby")
                {
                    var wall=AssetDatabase.LoadAssetAtPath<Material>("Assets/Candidate/Materials/Lobby/SM_AI_Wall.mat");
                    var pillar=AssetDatabase.LoadAssetAtPath<Material>("Assets/Candidate/Materials/Lobby/SM_AI_Pillar.mat");
                    foreach(var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        bool isWall=false,isPillar=false;
                        for(var t=renderer.transform;t!=null;t=t.parent) {isWall|=t.name.StartsWith("Wall");isPillar|=t.name.Contains("Pillar");}
                        if(!isWall&&!isPillar)continue;
                        var mats=renderer.sharedMaterials;
                        for(int i=0;i<mats.Length;i++)if(mats[i] && (mats[i].name=="SM_Floor" || mats[i].name=="SM_Wood"))mats[i]=isPillar?pillar:wall;
                        renderer.sharedMaterials=mats;
                        report.Add("Material hierarchy: "+renderer.name+" -> "+(isPillar?"pillar":"wall"));
                    }
                    EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }
                foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))light.shadows=LightShadows.None;
                var cameras=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
                var camera=cameras.OrderBy(c=>c.name).FirstOrDefault();
                if(!camera)camera=new GameObject("Review Camera",typeof(Camera)).GetComponent<Camera>();
                foreach(var c in cameras)c.enabled=false;
                camera.enabled=true;
                var extra=camera.GetUniversalAdditionalCameraData();
                extra.renderPostProcessing=false;
                camera.allowHDR=true;
                camera.allowMSAA=false;
                Render(camera,Path.Combine(output,mode+"_"+scene+".png"));
                report.Add(mode+" "+scene+" camera "+camera.transform.position+" / "+camera.transform.eulerAngles);
            }
            foreach(var id in AssetDatabase.FindAssets("t:Shader",new[]{"Assets/Candidate"}))
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(id));
                var compatibility=typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                if(compatibility!=null)report.Add(shader.name+" SRP Batcher code="+compatibility.Invoke(null,new object[]{shader,0}));
                var material=new Material(shader);
                foreach(var keys in new[]{new string[0],new[]{"_NORMAL_MAP","_EMISSION_MAP","_AO_MAP","_METALLIC_MAP","_SMOOTHNESS_MAP"},new[]{"LIGHTMAP_ON","DIRLIGHTMAP_COMBINED","SHADOWS_SHADOWMASK"}})
                {
                    material.shaderKeywords=keys;
                    for(int p=0;p<material.passCount;p++)report.Add(shader.name+" / "+string.Join(",",keys)+" / "+material.GetPassName(p)+" SetPass="+material.SetPass(p));
                }
                UnityEngine.Object.DestroyImmediate(material);
                foreach(var message in ShaderUtil.GetShaderMessages(shader))report.Add(message.severity+": "+message.message+" @ "+message.file+":"+message.line);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllLines(Path.Combine(output,"validation.txt"),report);
            EditorApplication.Exit(report.Any(x=>x.StartsWith("Error:"))?2:0);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(3);}
    }
    static void Render(Camera c,string file)
    {
        var rt=new RenderTexture(960,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        c.targetTexture=rt;c.aspect=960f/720f;
        c.Render();
        var old=RenderTexture.active;RenderTexture.active=rt;
        var tex=new Texture2D(960,720,TextureFormat.RGB24,false);
        tex.ReadPixels(new Rect(0,0,960,720),0,0);tex.Apply();
        File.WriteAllBytes(file,tex.EncodeToPNG());
        RenderTexture.active=old;c.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);
    }
}
