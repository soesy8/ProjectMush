using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Unity.Pipeline.Commands;
using Newtonsoft.Json.Linq;
using UObject=UnityEngine.Object;
public static class MushParkaFinalCheck20261006 {
 const string Evidence="E:/CodexTemp/2026-10-06/task-2";
 const string ScenePath="Assets/Scenes/PM_Lobby.unity";
 static Transform[] All(Scene s)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
 static Transform Find(Scene s,string n)=>All(s).First(t=>t.name==n);
 static void Require(bool b,string m){if(!b)throw new InvalidOperationException(m);}
 static void Write(string n,object o)=>File.WriteAllText(Evidence+"/"+n,Newtonsoft.Json.JsonConvert.SerializeObject(o,Newtonsoft.Json.Formatting.Indented));
 [CliCommand("mush_parka_reopen_check","Reopen the saved lobby and export actual Unity world-space Parka/scarf geometry without saving.")]
 public static object Check(){Require(!EditorApplication.isPlaying,"Stop Play Mode");var s=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);var records=new[]{"PM_WinterParka","PM_WinterScarf"}.Select(name=>{var root=Find(s,name+" Placement");var f=root.GetComponentsInChildren<MeshFilter>(true).Single();var mesh=f.sharedMesh;var renderer=f.GetComponent<MeshRenderer>();Require(renderer&&renderer.enabled&&renderer.sharedMaterials.Length==1,"Visual disconnected "+name);var vertices=mesh.vertices.Select(v=>f.transform.TransformPoint(v)).Select(v=>new[]{v.x,v.y,v.z}).ToArray();var uv=mesh.uv.Select(v=>new[]{v.x,v.y}).ToArray();var components=root.GetComponents<Component>().Select(c=>c.GetType().FullName).ToArray();return new{name,rootId=GlobalObjectId.GetGlobalObjectIdSlow(root).ToString(),parent=root.parent.name,localPosition=new[]{root.localPosition.x,root.localPosition.y,root.localPosition.z},localRotation=new[]{root.localRotation.x,root.localRotation.y,root.localRotation.z,root.localRotation.w},localScale=new[]{root.localScale.x,root.localScale.y,root.localScale.z},components,layer=root.gameObject.layer,tag=root.gameObject.tag,mesh=AssetDatabase.GetAssetPath(mesh),meshGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh)),material=AssetDatabase.GetAssetPath(renderer.sharedMaterial),texture=AssetDatabase.GetAssetPath(renderer.sharedMaterial.GetTexture("_BaseMap")),vertices,uv,triangles=mesh.triangles};}).ToArray();int missing=All(s).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));Require(missing==0,"Missing scripts");Write("parka_unity_world_geometry.json",new{scene=ScenePath,reopened=true,missingScripts=missing,records});return new{reopened=true,missingScripts=missing,records=records.Length,sceneDirty=s.isDirty};}
 [CliCommand("mush_parka_final_capture","Render the actual saved Unity door decorations after Parka/scarf comparison.")]
 public static object Capture(){var s=SceneManager.GetSceneByPath(ScenePath);var source=All(s).Select(t=>t.GetComponent<Camera>()).First(c=>c&&c.CompareTag("MainCamera")&&c.gameObject.activeInHierarchy);var go=new GameObject("Temporary Parka Final Camera"){hideFlags=HideFlags.HideAndDontSave};var c=go.AddComponent<Camera>();c.CopyFrom(source);c.enabled=false;var position=new Vector3(-2.1f,2.1f,3.5f);var target=new Vector3(-3.18f,1.44f,6.32f);c.transform.position=position;c.transform.rotation=Quaternion.LookRotation(target-position);c.fieldOfView=65;c.nearClipPlane=.03f;c.farClipPlane=80;var sd=source.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();var cd=go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();if(sd)EditorUtility.CopySerialized(sd,cd);cd.renderType=UnityEngine.Rendering.Universal.CameraRenderType.Base;cd.allowXRRendering=false;var rt=new RenderTexture(1440,900,24);rt.Create();var prior=RenderTexture.active;var path=Evidence+"/UnityLobby_winter/04_ParkaScarf_FinalVerified.png";try{c.targetTexture=rt;c.Render();RenderTexture.active=rt;var tex=new Texture2D(1440,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1440,900),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());UObject.DestroyImmediate(tex);}finally{RenderTexture.active=prior;c.targetTexture=null;rt.Release();UObject.DestroyImmediate(rt);UObject.DestroyImmediate(go);}return new{path};}
 [CliCommand("mush_parka_close","Close this final inspection Editor without saving scene changes.")]
 public static object Close(){Require(!EditorApplication.isPlaying,"Stop Play Mode");EditorApplication.delayCall+=()=>EditorApplication.Exit(0);return new{closing=true};}
}
