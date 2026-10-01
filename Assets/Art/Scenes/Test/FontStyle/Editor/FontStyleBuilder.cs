#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;

namespace Mush.Art.Test.FontStyle
{
    public static class FontStyleBuilder
    {
        public const string Root = "Assets/Art/Scenes/Test/FontStyle";
        public const string ScenePath = Root + "/FontStyle_Comparison.unity";
        static string Full(string path)
        {
            if (!(path == Root || path.StartsWith(Root + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Authoring is restricted to " + Root);
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }
        static void Folder(string sub) { Directory.CreateDirectory(Full(Root + "/" + sub)); }
        static TMP_FontAsset Load(string name) { return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/Fonts/" + name + ".asset"); }

        public static uint[] SupportedCharacters(string source)
        {
            var b = File.ReadAllBytes(source);
            Func<int,int> u16 = p => (b[p] << 8) | b[p + 1];
            Func<int,int> u32 = p => (b[p] << 24) | (b[p+1] << 16) | (b[p+2] << 8) | b[p+3];
            int cmap = 0;
            for (int i=0;i<u16(4);i++)
            { int p=12+i*16; if(System.Text.Encoding.ASCII.GetString(b,p,4)=="cmap") cmap=u32(p+8); }
            int sub=0, fmt=0;
            for (int i=0;i<u16(cmap+2);i++)
            {
                int p=cmap+4+i*8, candidate=cmap+u32(p+4), f=u16(candidate);
                if ((u16(p)==0 || u16(p)==3) && (f==4 || f==12) && (sub==0 || f==12)) { sub=candidate; fmt=f; }
            }
            if (sub==0) throw new InvalidDataException("No Unicode cmap");
            Func<int,int> glyph = cp =>
            {
                if(fmt==12)
                { for(int i=0;i<u32(sub+12);i++){int p=sub+16+i*12; if(cp>=u32(p)&&cp<=u32(p+4))return u32(p+8)+cp-u32(p);} return 0; }
                int n=u16(sub+6)/2, eb=sub+14, sb=eb+n*2+2, db=sb+n*2, rb=db+n*2;
                for(int i=0;i<n;i++)
                {
                    if(cp>u16(eb+i*2))continue;
                    int start=u16(sb+i*2); if(cp<start)return 0;
                    int delta=u16(db+i*2), range=u16(rb+i*2);
                    if(range==0)return(cp+delta)&65535;
                    int g=u16(rb+i*2+range+(cp-start)*2);
                    return g==0?0:(g+delta)&65535;
                }
                return 0;
            };
            return Enumerable.Range(32,95).Concat(Enumerable.Range(0x3131,94))
                .Concat(Enumerable.Range(0xAC00,11172))
                .Concat(new[]{160,8203,8230,9633,8211,8212,8216,8217,8220,8221,9734,9733})
                .Distinct().Where(cp=>glyph(cp)!=0).Select(cp=>(uint)cp).ToArray();
        }

        [MenuItem("Tools/Mush/Test Fonts/Build Fonts")]
        public static string BuildFonts()
        {
            Folder("Source"); Folder("Fonts"); Folder("Materials"); Folder("Prefabs"); Folder("Validation");
            string source=Root+"/Source/HSDuggobi.ttf";
            if(!File.Exists(Full(source)))
            {
                File.Copy(Path.Combine(Application.dataPath,"UI_Panel_Sample/Font/HS두꺼비체.ttf"),Full(source));
                AssetDatabase.ImportAsset(source,ImportAssetOptions.ForceSynchronousImport);
            }
            var font=AssetDatabase.LoadAssetAtPath<Font>(source);
            var chars=SupportedCharacters(Full(source));
            if(chars.Count(c=>c>=0xAC00&&c<=0xD7A3)!=2350)throw new InvalidDataException("Expected 2,350 Hangul syllables");
            var english=CreateFont(font,"Mush_Gold_English",80,15,1024,chars.Where(c=>c<0x3131 || c>0xD7A3).ToArray());
            var korean=CreateFont(font,"Mush_Gold_Korean_2350",50,9,4096,chars);
            english.fallbackFontAssetTable=new List<TMP_FontAsset>{korean};
            korean.fallbackFontAssetTable=new List<TMP_FontAsset>();
            EditorUtility.SetDirty(english); AssetDatabase.SaveAssetIfDirty(english);
            EditorUtility.SetDirty(korean); AssetDatabase.SaveAssetIfDirty(korean);
            string report=JsonConvert.SerializeObject(new{english=FontReport(english),korean=FontReport(korean)},Formatting.Indented);
            File.WriteAllText(Full(Root+"/Validation/FontCoverage.json"),report,new System.Text.UTF8Encoding(false));
            return report;
        }

        static TMP_FontAsset CreateFont(Font source,string name,int point,int padding,int atlas,uint[] chars)
        {
            var existing=Load(name);
            if(existing!=null)throw new InvalidOperationException("Font already exists: "+name);
            var f=TMP_FontAsset.CreateFontAsset(source,point,padding,GlyphRenderMode.SDFAA,atlas,atlas,AtlasPopulationMode.Dynamic,true);
            f.name=name;
            uint[] missing;
            if(!f.TryAddCharacters(chars,out missing,false)||(missing!=null&&missing.Length!=0))
                throw new InvalidOperationException(name+" missing: "+string.Join(",",missing??new uint[0]));
            f.atlasPopulationMode=AtlasPopulationMode.Static;
            f.isMultiAtlasTexturesEnabled=false;
            var face=f.faceInfo; face.scale=1; f.faceInfo=face;
            // The SDF is baked; runtime never changes the atlas or needs source font data.
            // Static atlas population disables runtime glyph rasterization.
            var m=f.material; m.name=name+"_GoldDepth";
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Shaders/GoldDepth_SDF.shader");
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Gold shader failed");
            m.shader=shader;
            m.EnableKeyword("BEVEL_ON"); m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_GoldTopColor",new Color(1,0.917647064f,0.627451f,1));
            m.SetColor("_GoldBottomColor",new Color(1,0.709803939f,0.168627456f,1));
            m.SetColor("_FaceColor",Color.white);
            m.SetColor("_OutlineColor",new Color(0.847058833f,0.5686274f,0.1333333f,1));
            m.SetColor("_DepthColor",new Color(0.768627465f,0.4078431f,0.06274508f,1));
            m.SetColor("_DepthOutlineColor",new Color(0.4823529f,0.199999958f,0.03529412f,1));
            m.SetColor("_UnderlayColor",new Color(0.119999953f,0.044999972f,0.01f,0.64f));
            // Preserve stroke and bevel widths in em units from the scene's 221 pt / 17 px atlas.
            float ratio=(18f/221f)/((padding+1f)/point);
            m.SetFloat("_FaceDilate",0.015f*ratio); m.SetFloat("_OutlineWidth",0.035f*ratio);
            m.SetFloat("_DepthDilate",0.025f*ratio); m.SetFloat("_DepthOutlineWidth",0.045f*ratio);
            m.SetFloat("_Bevel",0.55f); m.SetFloat("_BevelOffset",-0.02f*ratio);
            m.SetFloat("_BevelWidth",0.32f*ratio); m.SetFloat("_BevelRoundness",0.55f);
            m.SetFloat("_LightAngle",5.497787f); m.SetColor("_SpecularColor",new Color(1,0.9529411f,0.721568644f,1));
            m.SetFloat("_SpecularPower",1.1f); m.SetFloat("_Reflectivity",7); m.SetFloat("_Diffuse",0.28f); m.SetFloat("_Ambient",0.9f);
            // TMP underlay properties also reserve quad padding for the complete effect.
            m.SetFloat("_UnderlayOffsetX",0.42f); m.SetFloat("_UnderlayOffsetY",-0.78f);
            m.SetFloat("_UnderlayDilate",0.045f*ratio); m.SetFloat("_UnderlaySoftness",0.22f*ratio);
            ShaderUtilities.UpdateShaderRatios(m);
            float sdfEm=(padding+1f)/point;
            float sc=m.GetFloat("_ScaleRatioC");
            m.SetFloat("_FontEmScale",point/(padding+1f));
            m.SetFloat("_DepthOffsetX",0.03125f);
            m.SetFloat("_DepthOffsetY",-0.0625f);
            // Side object offset plus its existing underlay offset.
            float shadowEmX=0.03125f+0.4f*0.753194451f*18f/221f;
            float shadowEmY=-0.0625f-0.65f*0.753194451f*18f/221f;
            m.SetFloat("_ShadowEmX",shadowEmX);
            m.SetFloat("_ShadowEmY",shadowEmY);
            AssetDatabase.CreateAsset(m,Root+"/Materials/"+m.name+".mat");
            AssetDatabase.CreateAsset(f,Root+"/Fonts/"+name+".asset");
            for(int i=0;i<f.atlasTextures.Length;i++)
            {
                var tex=f.atlasTextures[i]; tex.name=name+"_Atlas_"+i;
                tex.hideFlags=HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(tex,f);
            }
            EditorUtility.SetDirty(f); AssetDatabase.SaveAssetIfDirty(f);
            m.SetTexture("_MainTex",f.atlasTextures[0]);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            AssetDatabase.ImportAsset(Root+"/Fonts/"+name+".asset",ImportAssetOptions.ForceSynchronousImport);
            return f;
        }

        static object FontReport(TMP_FontAsset f)
        {
            return new {name=f.name,path=AssetDatabase.GetAssetPath(f),mode=f.atlasPopulationMode.ToString(),
                characters=f.characterTable.Count,ascii=f.characterTable.Count(c=>c.unicode>=32&&c.unicode<=126),
                hangul=f.characterTable.Count(c=>c.unicode>=0xAC00&&c.unicode<=0xD7A3),
                jamo=f.characterTable.Count(c=>c.unicode>=0x3131&&c.unicode<=0x318E),
                samplingPointSize=f.faceInfo.pointSize,padding=f.atlasPadding,atlasCount=f.atlasTextures.Length,
                atlasWidth=f.atlasWidth,atlasHeight=f.atlasHeight,
                atlasMemoryBytes=f.atlasTextures.Sum(t=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t))};
        }

        static RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform));
            if(parent!=null)go.transform.SetParent(parent,false);
            var r=(RectTransform)go.transform; r.anchorMin=r.anchorMax=new Vector2(0.5f,0.5f);
            r.pivot=new Vector2(0.5f,0.5f); r.anchoredPosition=pos; r.sizeDelta=size; return r;
        }
        static TextMeshProUGUI Text(string name,Transform parent,Vector2 pos,Vector2 size,TMP_FontAsset font,string value,float pt,bool gold=true)
        {
            var rt=Rect(name,parent,pos,size);
            var t=rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font=font; t.fontSharedMaterial=font.material;
            t.text=value; t.fontSize=pt; t.enableAutoSizing=false; t.alignment=TextAlignmentOptions.Center;
            t.textWrappingMode=TextWrappingModes.NoWrap; t.overflowMode=TextOverflowModes.Overflow;
            t.color=Color.white; t.enableVertexGradient=false; t.raycastTarget=false;
            t.horizontalMapping=TextureMappingOptions.Character; t.verticalMapping=TextureMappingOptions.Character;
            t.extraPadding=true;
            return t;
        }
        static void Flat(TextMeshProUGUI t,Material flat)
        { t.fontSharedMaterial=flat; t.color=new Color(0.78f,0.80f,0.84f,1); }
        static void Panel(string name,Transform parent,Vector2 pos,Vector2 size)
        { var r=Rect(name,parent,pos,size); var i=r.gameObject.AddComponent<UnityEngine.UI.Image>(); i.color=new Color(0.11f,0.135f,0.16f,1); i.raycastTarget=false; }

        [MenuItem("Tools/Mush/Test Fonts/Build Comparison Scene")]
        public static string BuildComparison()
        {
            var en=Load("Mush_Gold_English"); var ko=Load("Mush_Gold_Korean_2350");
            if(en==null||ko==null)throw new InvalidOperationException("Build fonts first");
            var original=SceneManager.GetActiveScene();
            if(original.isDirty)throw new InvalidOperationException("Save user scene before building comparison");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var cameraGo=new GameObject("FontPreviewCamera",typeof(Camera));
            var camera=cameraGo.GetComponent<Camera>(); camera.orthographic=true; camera.orthographicSize=5;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(0.055f,0.07f,0.09f,1);
            camera.transform.position=new Vector3(0,0,-10);
            var canvasGo=new GameObject("FontComparisonCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var canvas=canvasGo.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=camera; canvas.planeDistance=1;
            var scaler=canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=0.5f;
            var flat=new Material(Shader.Find("TextMeshPro/Distance Field"));
            flat.name="Mush_FontComparison_Label"; flat.CopyPropertiesFromMaterial(en.material);
            flat.shader=Shader.Find("TextMeshPro/Distance Field"); flat.shaderKeywords=new string[0];
            flat.SetColor("_FaceColor",Color.white); flat.SetFloat("_FaceDilate",0); flat.SetFloat("_OutlineWidth",0);
            AssetDatabase.CreateAsset(flat,Root+"/Materials/"+flat.name+".mat");
            var heading=Text("Heading",canvasGo.transform,new Vector2(0,465),new Vector2(1800,80),en,"HSDuggobi  /  Gold Depth Fonts",48);
            var desc=Text("Description",canvasGo.transform,new Vector2(0,407),new Vector2(1800,50),en,"English + 2,350 Korean syllables   |   One TMP text per sample",24); Flat(desc,flat);
            Panel("OriginalPanel",canvasGo.transform,new Vector2(-455,248),new Vector2(860,220));
            Panel("MergedPanel",canvasGo.transform,new Vector2(455,248),new Vector2(860,220));
            var l=Text("OriginalLabel",canvasGo.transform,new Vector2(-455,328),new Vector2(800,40),en,"CURRENT SCENE / TWO TEXTS",24); Flat(l,flat);
            var r=Text("MergedLabel",canvasGo.transform,new Vector2(455,328),new Vector2(800,40),en,"NEW ENGLISH / ONE TEXT",24); Flat(r,flat);
            var oldFont=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI_Panel_Sample/Font/HSDuggobi SDF.asset");
            var side=Text("Original_GoldSideAndShadow",canvasGo.transform,new Vector2(-452.5f,225),new Vector2(820,160),oldFont,"Customize",80);
            side.fontSharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Scenes/Test/HSDuggobi SDF Material_FontDepthSide.mat");
            var face=Text("Original_GoldFace",canvasGo.transform,new Vector2(-455,230),new Vector2(820,160),oldFont,"Customize",80);
            face.fontSharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Scenes/Test/HSDuggobi SDF Material_FontDepthFace.mat");
            face.enableVertexGradient=true;
            face.colorGradient=new VertexGradient(new Color(1,0.917647064f,0.627451f),new Color(1,0.917647064f,0.627451f),new Color(1,0.709803939f,0.168627456f),new Color(1,0.709803939f,0.168627456f));
            Text("English80",canvasGo.transform,new Vector2(455,230),new Vector2(820,160),en,"Customize",80);
            Panel("KoreanPanel",canvasGo.transform,new Vector2(0,25),new Vector2(1770,180));
            var kl=Text("KoreanLabel",canvasGo.transform,new Vector2(0,88),new Vector2(1700,40),en,"KOREAN 2,350 / ENGLISH + KOREAN TOGETHER",24); Flat(kl,flat);
            Text("Korean64",canvasGo.transform,new Vector2(0,9),new Vector2(1700,130),ko,"커스터마이즈   출발하기   배달 완료!",64);
            Panel("SizesPanel",canvasGo.transform,new Vector2(0,-206),new Vector2(1770,215));
            Text("Mixed48",canvasGo.transform,new Vector2(0,-149),new Vector2(1700,95),en,"KAI 준비 123   /   LUMI 출발   /   옵션",48);
            Text("Mixed32",canvasGo.transform,new Vector2(0,-219),new Vector2(1700,65),ko,"주행 시간 00:12.34   최고 기록   로비로 돌아가기",32);
            Text("Mixed24",canvasGo.transform,new Vector2(0,-274),new Vector2(1700,50),en,"English 24pt   /   한글 24pt   /   0123456789",24);
            var mr=Rect("MaskSample",canvasGo.transform,new Vector2(-440,-410),new Vector2(825,115));
            var mi=mr.gameObject.AddComponent<UnityEngine.UI.Image>(); mi.color=new Color(0.14f,0.17f,0.20f,1);
            var mask=mr.gameObject.AddComponent<UnityEngine.UI.Mask>(); mask.showMaskGraphic=true;
            Text("MaskedText",mr,new Vector2(190,0),new Vector2(1300,100),en,"MASK TEST / 마스크 안의 금색 글자",44);
            var rr=Rect("RectMaskSample",canvasGo.transform,new Vector2(440,-410),new Vector2(825,115));
            var ri=rr.gameObject.AddComponent<UnityEngine.UI.Image>(); ri.color=mi.color;
            rr.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            Text("RectMaskedText",rr,new Vector2(190,0),new Vector2(1300,100),ko,"CLIP TEST / 잘리는 입체 글자",44);
            CreatePrefab(en,"GoldText_English","Customize");
            CreatePrefab(ko,"GoldText_Korean_2350","커스터마이즈");
            Canvas.ForceUpdateCanvases();
            foreach(var t in canvasGo.GetComponentsInChildren<TMP_Text>())t.ForceMeshUpdate(true,true);
            if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new IOException("Failed saving comparison scene");
            EditorSceneManager.CloseScene(scene,true);
            EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            return ScenePath;
        }

        static void CreatePrefab(TMP_FontAsset font,string name,string sample)
        {
            var t=Text(name,null,Vector2.zero,new Vector2(720,140),font,sample,80);
            PrefabUtility.SaveAsPrefabAsset(t.gameObject,Root+"/Prefabs/"+name+".prefab");
            UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        [MenuItem("Tools/Mush/Test Fonts/Validate Coverage")]
        public static string Validate()
        {
            var en=Load("Mush_Gold_English");var ko=Load("Mush_Gold_Korean_2350");
            var expected=SupportedCharacters(Full(Root+"/Source/HSDuggobi.ttf")).Where(c=>c>=0xAC00&&c<=0xD7A3).ToArray();
            var actual=new HashSet<uint>(ko.characterTable.Select(c=>c.unicode));
            var missing=expected.Where(c=>!actual.Contains(c)).ToArray();
            var texErrors=new List<string>();
            foreach(var f in new[]{en,ko})
            foreach(var c in f.characterTable)
            {
                var g=c.glyph;
                if(g.atlasIndex<0||g.atlasIndex>=f.atlasTextures.Length)texErrors.Add(f.name+":"+c.unicode);
                else if(g.glyphRect.x<0||g.glyphRect.y<0||g.glyphRect.x+g.glyphRect.width>f.atlasWidth||g.glyphRect.y+g.glyphRect.height>f.atlasHeight)texErrors.Add(f.name+":"+c.unicode);
            }
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Shaders/GoldDepth_SDF.shader");
            var scene=SceneManager.GetActiveScene();
            var texts=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TMP_Text>(true)).ToArray();
            foreach(var t in texts)t.ForceMeshUpdate(true,true);
            var unresolved=texts.SelectMany(t=>t.textInfo.characterInfo.Take(t.textInfo.characterCount).Where(c=>!char.IsWhiteSpace(c.character)&&(c.textElement==null||c.textElement.unicode!=c.character)).Select(c=>t.name+":"+c.character)).ToArray();
            var report=new{hangulExpected=expected.Length,hangulMissing=missing,atlasErrors=texErrors,shaderErrors=ShaderUtil.GetShaderMessages(shader).Select(m=>new{m.message,m.severity}).ToArray(),
                unresolvedCharacters=unresolved,english=FontReport(en),korean=FontReport(ko),
                textObjects=texts.Length,comparisonScene=scene.path,
                samples=texts.Select(t=>new{name=t.name,characters=t.textInfo.characterCount,materials=t.textInfo.materialCount}).ToArray()};
            string json=JsonConvert.SerializeObject(report,Formatting.Indented);
            File.WriteAllText(Full(Root+"/Validation/Verification.json"),json,new System.Text.UTF8Encoding(false));
            if(missing.Length!=0||texErrors.Count!=0||ShaderUtil.ShaderHasError(shader)||unresolved.Length!=0)throw new InvalidOperationException(json);
            return json;
        }

        public static string RenderPreview()
        {
            if(SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Open comparison scene");
            var camera=GameObject.Find("FontPreviewCamera").GetComponent<Camera>();
            var rt=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGB32);
            var old=camera.targetTexture;var active=RenderTexture.active;
            Texture2D tex=null;
            try
            {
                Canvas.ForceUpdateCanvases();
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);
                tex.ReadPixels(new UnityEngine.Rect(0,0,1920,1080),0,0);tex.Apply();
                File.WriteAllBytes(Full(Root+"/Validation/FontStyle_Preview.png"),tex.EncodeToPNG());
            }
            finally {camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);if(tex!=null)UnityEngine.Object.DestroyImmediate(tex);}
            return Full(Root+"/Validation/FontStyle_Preview.png");
        }
    }
}
#endif
