using System.Linq;
using Mush.Customization;
using UnityEditor;
using UnityEngine;

namespace Mush.Customization.Editor
{
    [InitializeOnLoad]
    public static class MushCustomizationCatalogRepair
    {
        private const string CatalogPath = "Assets/Resources/MushCustomizationCatalog.asset";
        private const string StoreScenePath = "Assets/Scenes/MushStore.unity";
        private const string HousingScenePath = "Assets/Scenes/MushHousing.unity";

        static MushCustomizationCatalogRepair()
        {
            EditorApplication.delayCall += RepairReferences;
        }

        [MenuItem("Mush/Customization/Repair Catalog References")]
        public static void RepairReferences()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            MushCustomizationCatalog catalog = AssetDatabase.LoadAssetAtPath<MushCustomizationCatalog>(CatalogPath);
            if (catalog == null)
                return;

            bool changed = false;
            changed |= Assign(ref catalog.koreanFont, "Assets/Font/Hakgyoansim_PosterB.ttf");
            changed |= Assign(ref catalog.lobbyEnvironment, "Assets/Scenes/Mush_Lobby.fbx");
            changed |= Assign(ref catalog.sledNatural, "Assets/Scenes/Mush_Sled_Natural.fbx");
            changed |= Assign(ref catalog.sledRed, "Assets/Scenes/Mush_Sled_Red.fbx");
            changed |= Assign(ref catalog.sledBlue, "Assets/Scenes/Mush_Sled_Blue.fbx");
            changed |= Assign(ref catalog.sledBlack, "Assets/Scenes/Mush_Sled_Black.fbx");
            changed |= Assign(ref catalog.sledSanta, "Assets/Scenes/Mush_Sled_Santa.fbx");
            changed |= Assign(ref catalog.sledFrontLantern, "Assets/Scenes/Mush_Sled_FrontLantern.fbx");
            changed |= Assign(ref catalog.husky, "Assets/MushLobby/Dogs/Models/Mush_LowPoly_Husky.fbx");
            changed |= Assign(ref catalog.malamute, "Assets/MushLobby/Dogs/Models/Mush_LowPoly_Malamute.fbx");
            changed |= Assign(ref catalog.huskyFedora, "Assets/Scenes/Mush_Husky_Fedora.fbx");
            changed |= Assign(ref catalog.malamuteFedora, "Assets/Scenes/Mush_Malamute_Fedora.fbx");
            changed |= Assign(ref catalog.huskySantaHat, "Assets/Scenes/Mush_Husky_SantaHat.fbx");
            changed |= Assign(ref catalog.malamuteSantaHat, "Assets/Scenes/Mush_Malamute_SantaHat.fbx");
            changed |= Assign(ref catalog.huskyPurpleScarf, "Assets/Scenes/Mush_Husky_PurpleScarf.fbx");
            changed |= Assign(ref catalog.malamutePurpleScarf, "Assets/Scenes/Mush_Malamute_PurpleScarf.fbx");
            changed |= Assign(ref catalog.huskyRedBandana, "Assets/Scenes/Mush_Husky_RedBandana.fbx");
            changed |= Assign(ref catalog.malamuteRedBandana, "Assets/Scenes/Mush_Malamute_RedBandana.fbx");
            changed |= Assign(ref catalog.furnitureTable, "Assets/Scenes/Mush_Furniture_SmallTable.fbx");
            changed |= Assign(ref catalog.furnitureChair, "Assets/Scenes/Mush_Furniture_CozyChair.fbx");
            changed |= Assign(ref catalog.furnitureDogBed, "Assets/Scenes/Mush_Furniture_DogBed.fbx");

            if (changed)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            EnsureCustomizationScenesInBuildSettings();
        }

        private static bool Assign<T>(ref T field, string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (field == asset)
                return false;
            field = asset;
            return true;
        }

        private static void EnsureCustomizationScenesInBuildSettings()
        {
            bool hasStore = EditorBuildSettings.scenes.Any(scene => scene.path == StoreScenePath && scene.enabled);
            bool hasHousing = EditorBuildSettings.scenes.Any(scene => scene.path == HousingScenePath && scene.enabled);
            if (hasStore && hasHousing)
                return;

            var scenes = EditorBuildSettings.scenes.ToList();
            if (!hasStore)
            {
                scenes.RemoveAll(scene => scene.path == StoreScenePath);
                scenes.Add(new EditorBuildSettingsScene(StoreScenePath, true));
            }
            if (!hasHousing)
            {
                scenes.RemoveAll(scene => scene.path == HousingScenePath);
                scenes.Add(new EditorBuildSettingsScene(HousingScenePath, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
