using System.Linq;
using Mush.Customization;
using UnityEditor;
using UnityEngine;

namespace Mush.Customization.Editor
{
    public static class MushCustomizationCatalogRepair
    {
        private const string CatalogPath = "Assets/Resources/MushCustomizationCatalog.asset";
        private const string StoreScenePath = "Assets/Mush/Scenes/MushStore.unity";
        private const string HousingScenePath = "Assets/Mush/Scenes/MushHousing.unity";

        public static void RepairReferences()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            MushCustomizationCatalog catalog = AssetDatabase.LoadAssetAtPath<MushCustomizationCatalog>(CatalogPath);
            if (catalog == null)
                return;

            bool changed = false;
            changed |= Assign(ref catalog.koreanFont, "Assets/UI/UI_Panel_Sample/Font/HS두꺼비체.ttf");
            changed |= Assign(ref catalog.uiPanelPrefab, "Assets/UI/UI_Panel_Sample/Prefab/LobbyUI_Panel.prefab");
            changed |= Assign(ref catalog.lobbyEnvironment, "Assets/Mush/Scenes/Mush_Lobby.fbx");
            changed |= Assign(ref catalog.sledNatural, "Assets/Mush/Scenes/Mush_Sled_Natural.fbx");
            changed |= Assign(ref catalog.sledRed, "Assets/Mush/Scenes/Mush_Sled_Red.fbx");
            changed |= Assign(ref catalog.sledBlue, "Assets/Mush/Scenes/Mush_Sled_Blue.fbx");
            changed |= Assign(ref catalog.sledBlack, "Assets/Mush/Scenes/Mush_Sled_Black.fbx");
            changed |= Assign(ref catalog.sledSanta, "Assets/Mush/Scenes/Mush_Sled_Santa.fbx");
            changed |= Assign(ref catalog.sledFrontLantern, "Assets/Mush/Scenes/Mush_Sled_FrontLantern.fbx");
            changed |= Assign(ref catalog.husky, "Assets/Mush/Lobby/Dogs/Models/Mush_LowPoly_Husky.fbx");
            changed |= Assign(ref catalog.malamute, "Assets/Mush/Lobby/Dogs/Models/Mush_LowPoly_Malamute.fbx");
            changed |= Assign(ref catalog.huskyFedora, "Assets/Mush/Scenes/Mush_Husky_Fedora.fbx");
            changed |= Assign(ref catalog.malamuteFedora, "Assets/Mush/Scenes/Mush_Malamute_Fedora.fbx");
            changed |= Assign(ref catalog.huskySantaHat, "Assets/Mush/Scenes/Mush_Husky_SantaHat.fbx");
            changed |= Assign(ref catalog.malamuteSantaHat, "Assets/Mush/Scenes/Mush_Malamute_SantaHat.fbx");
            changed |= Assign(ref catalog.huskyPurpleScarf, "Assets/Mush/Scenes/Mush_Husky_PurpleScarf.fbx");
            changed |= Assign(ref catalog.malamutePurpleScarf, "Assets/Mush/Scenes/Mush_Malamute_PurpleScarf.fbx");
            changed |= Assign(ref catalog.huskyRedBandana, "Assets/Mush/Scenes/Mush_Husky_RedBandana.fbx");
            changed |= Assign(ref catalog.malamuteRedBandana, "Assets/Mush/Scenes/Mush_Malamute_RedBandana.fbx");
            changed |= Assign(ref catalog.furnitureTable, "Assets/Mush/Scenes/Mush_Furniture_SmallTable.fbx");
            changed |= Assign(ref catalog.furnitureChair, "Assets/Mush/Scenes/Mush_Furniture_CozyChair.fbx");
            changed |= Assign(ref catalog.furnitureDogBed, "Assets/Mush/Scenes/Mush_Furniture_DogBed.fbx");

            if (changed)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            EnsureCustomizationScenesInBuildSettings();
        }

        private static bool Assign<T>(ref T field, string path) where T : Object
        {
            if (field != null)
                return false;

            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
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
