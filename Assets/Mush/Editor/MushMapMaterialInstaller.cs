#if UNITY_EDITOR // 재질 Asset 생성과 FBX remap은 Editor에서만 수행한다.

using System; // StringComparison 등 문자열 비교 기능을 사용한다.
using System.Collections.Generic; // 재질 사양 Dictionary와 FBX 경로 목록을 관리한다.
using System.IO; // 파일명과 폴더 경로를 안전하게 다룬다.
using UnityEditor; // AssetDatabase, AssetImporter, MenuItem 등 Editor API를 사용한다.
using UnityEngine; // Material, Shader, Color 등 Unity 기본 타입을 사용한다;

/// <summary>
/// Project : Mush Map V2 FBX가 가진 재질 이름을 기준으로 URP/Lit .mat를 생성하고
/// AssetImporter.AddRemap을 사용해 FBX 내부 재질 슬롯을 외부 Material Asset에 영구 연결한다.
/// </summary>
public static class MushMapMaterialInstaller // V2 맵 재질 설치/연결 전용 Editor 도구다.
{
    private const string MaterialFolder = "Assets/Mush/Generated/MapMaterials_V2"; // 생성한 URP 재질을 한 곳에 보관한다.

    private readonly struct Spec // 재질 이름별 BaseColor, Smoothness, Metallic, Emission 값을 저장한다.
    {
        public readonly Color color; // URP/Lit Base Color다.
        public readonly float smoothness; // URP/Lit Smoothness다.
        public readonly float metallic; // URP/Lit Metallic이다.
        public readonly Color emission; // HDR Emission이며 검정이면 Emission을 끈다.

        public Spec(Color color, float smoothness, float metallic, Color emission) // 재질 한 종류의 값을 생성한다.
        {
            this.color = color; // Base Color를 저장한다.
            this.smoothness = smoothness; // Smoothness를 저장한다.
            this.metallic = metallic; // Metallic을 저장한다.
            this.emission = emission; // Emission을 저장한다.
        }
    }

    private static readonly Dictionary<string, Spec> Specs = new(StringComparer.Ordinal) // Blender와 동일한 정확한 재질 이름을 키로 사용한다.
    {
        ["MUSH_MAT_Snow"] = new Spec(new Color(0.82f,0.89f,0.96f,1f), 0.16f, 0f, Color.black), // 푸른 기가 있는 흰색 눈이다.
        ["MUSH_MAT_PackedSnow"] = new Spec(new Color(0.48f,0.58f,0.68f,1f), 0.22f, 0f, Color.black), // 도로가 눈밭과 확실히 구분되도록 더 진한 청회색 압설을 사용한다.
        ["MUSH_MAT_SledTrack"] = new Spec(new Color(0.32f,0.41f,0.51f,1f), 0.18f, 0f, Color.black), // Road Mesh 안의 두 줄 썰매 러너 자국이다.
        ["MUSH_MAT_PineDark"] = new Spec(new Color(0.04f,0.12f,0.07f,1f), 0.12f, 0f, Color.black), // 짙은 소나무색이다.
        ["MUSH_MAT_PineMid"] = new Spec(new Color(0.08f,0.22f,0.12f,1f), 0.12f, 0f, Color.black), // 중간 소나무색이다.
        ["MUSH_MAT_Wood"] = new Spec(new Color(0.30f,0.15f,0.06f,1f), 0.16f, 0f, Color.black), // 줄기/울타리 갈색이다.
        ["MUSH_MAT_WoodLight"] = new Spec(new Color(0.48f,0.28f,0.10f,1f), 0.18f, 0f, Color.black), // 밝은 목재색이다.
        ["MUSH_MAT_Rock"] = new Spec(new Color(0.28f,0.34f,0.40f,1f), 0.12f, 0f, Color.black), // 청회색 바위다.
        ["MUSH_MAT_RouteRed"] = new Spec(new Color(0.58f,0.05f,0.04f,1f), 0.20f, 0f, Color.black), // 붉은 경로 표지다.
        ["MUSH_MAT_BeaconGlow"] = new Spec(new Color(1.00f,0.32f,0.03f,1f), 0.28f, 0f, new Color(3.2f,0.75f,0.03f,1f)), // 주황 발광 경광봉이다.
        ["MUSH_MAT_Ice"] = new Spec(new Color(0.42f,0.63f,0.77f,1f), 0.68f, 0f, Color.black), // 얼어붙은 호수/개울 재질이다.
        ["MUSH_MAT_NightSky"] = new Spec(new Color(0.015f,0.030f,0.110f,1f), 0.08f, 0f, Color.black), // 짙은 남색 밤하늘용 색이다.
        ["MUSH_MAT_Star"] = new Spec(new Color(0.75f,0.88f,1.00f,1f), 0.35f, 0f, new Color(2.8f,3.3f,4.0f,1f)), // 청백색 별 발광이다.
        ["MUSH_MAT_Sign"] = new Spec(new Color(0.72f,0.52f,0.20f,1f), 0.16f, 0f, Color.black), // 목적지 표지판의 밝은 나무색이다.
    };

    [MenuItem("Mush/Maps/V2/URP 재질 생성 + 모든 V2 FBX 연결")] // 한 번에 프로젝트의 V2 FBX를 찾아 영구 remap하는 메뉴다.
    public static void InstallAllV2Materials() // 사용자가 FBX를 Assets에 넣은 뒤 이 메뉴 한 번만 실행하면 된다.
    {
        EnsureFolder("Assets/Mush"); // 상위 Mush 폴더를 보장한다.
        EnsureFolder("Assets/Mush/Generated"); // 자동 생성 폴더를 보장한다.
        EnsureFolder(MaterialFolder); // 최종 재질 폴더를 보장한다.

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit"); // 현재 URP 프로젝트에 포함된 Lit Shader를 찾는다.
        if (urpLit == null) throw new InvalidOperationException("Universal Render Pipeline/Lit Shader를 찾지 못했습니다. URP 프로젝트인지 확인하세요."); // URP가 아니면 잘못된 재질을 만들지 않고 즉시 중단한다.

        Dictionary<string, Material> materials = new(StringComparer.Ordinal); // 생성/재사용한 Material Asset을 이름별로 보관한다.

        foreach (KeyValuePair<string, Spec> pair in Specs) // Blender에서 사용하는 모든 재질 이름을 하나씩 처리한다.
        {
            string materialPath = $"{MaterialFolder}/{pair.Key}.mat"; // 재질 이름 자체를 .mat 파일명으로 사용한다.
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath); // 기존 V2 재질 Asset이 있으면 다시 사용한다.

            if (material == null) // 처음 실행한 재질이면 새 Asset을 생성한다.
            {
                material = new Material(urpLit) { name = pair.Key }; // 반드시 URP/Lit 기반 Material을 만든다.
                AssetDatabase.CreateAsset(material, materialPath); // import callback 밖의 명시적 Editor 메뉴이므로 정상적으로 Asset을 생성한다.
            }
            else if (material.shader != urpLit) // 기존 파일이 다른 Shader를 쓰고 있으면 V2 규격으로 되돌린다.
            {
                material.shader = urpLit; // URP/Lit을 강제한다.
            }

            ApplySpec(material, pair.Value); // Base Color, Smoothness, Metallic, Emission 값을 실제 Material에 설정한다.
            EditorUtility.SetDirty(material); // 변경된 Material이 저장되어야 한다고 Editor에 알린다.
            materials[pair.Key] = material; // FBX remap 단계에서 바로 찾을 수 있게 저장한다.
        }

        AssetDatabase.SaveAssets(); // 생성/변경된 모든 .mat를 디스크에 먼저 저장한다.

        string[] modelGuids = AssetDatabase.FindAssets("t:Model"); // 프로젝트의 모든 Model Asset GUID를 검색한다.
        int remappedModels = 0; // 실제로 V2 FBX를 처리한 수를 센다.

        foreach (string guid in modelGuids) // 검색된 Model Asset을 하나씩 확인한다.
        {
            string path = AssetDatabase.GUIDToAssetPath(guid); // GUID를 Assets 기준 실제 경로로 변환한다.
            if (!path.EndsWith("_V2.fbx", StringComparison.OrdinalIgnoreCase)) continue; // V2 파일만 대상으로 하여 기존 FBX는 절대 건드리지 않는다.
            if (!Path.GetFileName(path).StartsWith("Mush_Map", StringComparison.OrdinalIgnoreCase)) continue; // Project Mush 맵/소품 FBX에만 적용한다.

            AssetImporter importer = AssetImporter.GetAtPath(path); // FBX의 실제 Importer를 가져온다.
            if (importer == null) continue; // Importer가 없는 비정상 Asset은 건너뛴다.

            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path); // FBX 내부 Material sub-asset 이름을 실제로 확인한다.
            bool changed = false; // remap이 하나라도 발생했는지 기록한다.

            foreach (UnityEngine.Object subAsset in subAssets) // FBX 내부 모든 sub-asset을 확인한다.
            {
                if (subAsset is not Material sourceMaterial) continue; // Material만 remap 대상으로 사용한다.
                string canonicalName = CanonicalMaterialName(sourceMaterial.name); // .001 같은 importer suffix가 붙어도 원래 Blender 재질 이름으로 환원한다.
                if (!materials.TryGetValue(canonicalName, out Material externalMaterial)) continue; // V2 사양에 존재하는 재질만 연결한다.

                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(sourceMaterial), externalMaterial); // FBX 내부 재질을 외부 URP/Lit .mat에 영구 remap한다.
                changed = true; // 이 FBX에 실제 변경이 있었음을 기록한다.
            }

            if (changed) // remap이 있는 FBX만 다시 import한다.
            {
                AssetDatabase.WriteImportSettingsIfDirty(path); // .meta에 remap 정보를 기록한다.
                importer.SaveAndReimport(); // FBX Instance가 즉시 외부 URP/Lit 재질을 사용하도록 재임포트한다.
                remappedModels++; // 처리 수를 증가시킨다.
            }
        }

        AssetDatabase.SaveAssets(); // 모든 FBX remap과 Material을 최종 저장한다.
        AssetDatabase.Refresh(); // Project 창을 갱신한다.
        Debug.Log($"[Mush Map V2] URP/Lit 재질 {materials.Count}개 생성/갱신, V2 FBX {remappedModels}개 remap 완료."); // 결과를 Console에 명확히 남긴다.
    }

    private static void ApplySpec(Material material, Spec spec) // URP/Lit의 실제 프로퍼티에 V2 색상 사양을 적용한다.
    {
        if (material.HasColor("_BaseColor")) material.SetColor("_BaseColor", spec.color); // URP/Lit Base Color를 설정한다.
        material.color = spec.color; // Material.color도 같은 값으로 맞춰 Inspector/다른 코드에서도 일치하게 한다.
        if (material.HasFloat("_Smoothness")) material.SetFloat("_Smoothness", spec.smoothness); // Smoothness를 사양대로 설정한다.
        if (material.HasFloat("_Metallic")) material.SetFloat("_Metallic", spec.metallic); // Metallic을 사양대로 설정한다.
        material.enableInstancing = true; // 동일 재질을 사용하는 반복 환경물에서 GPU Instancing을 사용할 수 있게 한다.

        bool useEmission = spec.emission.maxColorComponent > 0.001f; // 검정이 아닌 Emission 사양인지 검사한다.
        if (useEmission) // 경광봉/별처럼 실제 발광이 필요한 재질이다.
        {
            material.EnableKeyword("_EMISSION"); // URP/Lit Emission Shader Variant를 켠다.
            if (material.HasColor("_EmissionColor")) material.SetColor("_EmissionColor", spec.emission); // HDR 발광색을 설정한다.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; // 프로토타입 런타임 발광이 베이크 GI에 불필요하게 관여하지 않게 한다.
        }
        else // 일반 환경 재질은 Emission을 사용하지 않는다.
        {
            material.DisableKeyword("_EMISSION"); // 불필요한 Emission Variant를 끈다.
            if (material.HasColor("_EmissionColor")) material.SetColor("_EmissionColor", Color.black); // 남아 있는 발광값도 검정으로 초기화한다.
        }
    }

    private static string CanonicalMaterialName(string sourceName) // Blender/FBX round-trip에서 생길 수 있는 .001 숫자 suffix를 제거한다.
    {
        int dotIndex = sourceName.LastIndexOf('.'); // 마지막 점 위치를 찾는다.
        if (dotIndex <= 0 || dotIndex >= sourceName.Length - 1) return sourceName; // suffix 형태가 아니면 원래 이름을 그대로 쓴다.
        string suffix = sourceName[(dotIndex + 1)..]; // 점 뒤 문자열을 가져온다.
        if (int.TryParse(suffix, out _)) return sourceName[..dotIndex]; // 숫자만 있는 suffix면 Blender 중복 suffix로 보고 제거한다.
        return sourceName; // 일반 이름의 점은 보존한다.
    }

    private static void EnsureFolder(string path) // AssetDatabase.CreateFolder가 부모 폴더를 요구하므로 단계별로 안전하게 생성한다.
    {
        if (AssetDatabase.IsValidFolder(path)) return; // 이미 존재하면 이름 뒤에 숫자가 붙는 중복 폴더를 만들지 않는다.
        string parent = Path.GetDirectoryName(path)?.Replace("\\", "/"); // Unity Asset 경로 형식으로 부모 경로를 구한다.
        string name = Path.GetFileName(path); // 마지막 폴더 이름만 분리한다.
        if (!string.IsNullOrEmpty(parent) && parent != "Assets") EnsureFolder(parent); // 더 깊은 폴더라면 부모부터 재귀적으로 만든다.
        AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, name); // 실제 Unity Asset 폴더를 생성한다.
    }
}

#endif // UNITY_EDITOR
