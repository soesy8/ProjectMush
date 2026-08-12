using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Mush.Customization
{
    public enum MushItemCategory
    {
        Sled,
        Dog,
        Housing,
    }

    public enum MushEquipmentSlot
    {
        SledBody,
        SledDecoration,
        DogHat,
        DogNeck,
        Housing,
    }

    public static class MushCustomizationIds
    {
        public const string SledNatural = "sled_natural";
        public const string SledRed = "sled_red";
        public const string SledBlue = "sled_blue";
        public const string SledBlack = "sled_black";
        public const string SledSanta = "sled_santa";
        public const string SledLantern = "sled_lantern";

        public const string DogFedora = "dog_fedora";
        public const string DogSantaHat = "dog_santa_hat";
        public const string DogPurpleScarf = "dog_purple_scarf";
        public const string DogRedBandana = "dog_red_bandana";

        public const string FurnitureTable = "furniture_table";
        public const string FurnitureChair = "furniture_chair";
        public const string FurnitureDogBed = "furniture_dog_bed";

        // Placement-only values. They are always available and are not shop
        // products, so they never appear in the owned item list.
        public const string HousingDefaultDogCare = "housing_default_dog_care";
    }

    public static class MushHousingLayout
    {
        public const int ChairPlacement = 0;
        public const int TablePlacement = 1;
        public const int DogRestPlacement = 2;
        public const int PlacementCount = 3;

        public static Vector3 Position(int placementIndex)
        {
            return placementIndex switch
            {
                ChairPlacement => new Vector3(1.35f, 0f, -1.15f),
                TablePlacement => new Vector3(1.55f, 0f, 0.25f),
                DogRestPlacement => new Vector3(-1.70f, 0f, -0.40f),
                _ => Vector3.zero,
            };
        }

        public static Quaternion Rotation(int placementIndex)
        {
            float yaw = placementIndex switch
            {
                ChairPlacement => -28f,
                TablePlacement => 0f,
                DogRestPlacement => 8f,
                _ => 0f,
            };
            return Quaternion.Euler(0f, yaw, 0f);
        }

        public static float PreviewSize(int placementIndex)
        {
            return placementIndex switch
            {
                ChairPlacement => 1.22f,
                TablePlacement => 1.05f,
                DogRestPlacement => 1.28f,
                _ => 1f,
            };
        }
    }

    public sealed class MushCustomizationItemDefinition
    {
        public readonly string id;
        public readonly string displayName;
        public readonly MushItemCategory category;
        public readonly MushEquipmentSlot slot;

        public MushCustomizationItemDefinition(
            string newId,
            string newDisplayName,
            MushItemCategory newCategory,
            MushEquipmentSlot newSlot)
        {
            id = newId;
            displayName = newDisplayName;
            category = newCategory;
            slot = newSlot;
        }
    }

    public static class MushCustomizationDatabase
    {
        public static readonly MushCustomizationItemDefinition[] Items =
        {
            new(MushCustomizationIds.SledNatural, "기본 썰매", MushItemCategory.Sled, MushEquipmentSlot.SledBody),
            new(MushCustomizationIds.SledRed, "빨간 썰매", MushItemCategory.Sled, MushEquipmentSlot.SledBody),
            new(MushCustomizationIds.SledBlue, "파란 썰매", MushItemCategory.Sled, MushEquipmentSlot.SledBody),
            new(MushCustomizationIds.SledBlack, "검은 썰매", MushItemCategory.Sled, MushEquipmentSlot.SledBody),
            new(MushCustomizationIds.SledSanta, "산타 썰매", MushItemCategory.Sled, MushEquipmentSlot.SledBody),
            new(MushCustomizationIds.SledLantern, "앞 등불", MushItemCategory.Sled, MushEquipmentSlot.SledDecoration),
            new(MushCustomizationIds.DogFedora, "중절모", MushItemCategory.Dog, MushEquipmentSlot.DogHat),
            new(MushCustomizationIds.DogSantaHat, "산타 모자", MushItemCategory.Dog, MushEquipmentSlot.DogHat),
            new(MushCustomizationIds.DogPurpleScarf, "보라 스카프", MushItemCategory.Dog, MushEquipmentSlot.DogNeck),
            new(MushCustomizationIds.DogRedBandana, "빨간 반다나", MushItemCategory.Dog, MushEquipmentSlot.DogNeck),
            new(MushCustomizationIds.FurnitureTable, "작은 탁자", MushItemCategory.Housing, MushEquipmentSlot.Housing),
            new(MushCustomizationIds.FurnitureChair, "포근한 의자", MushItemCategory.Housing, MushEquipmentSlot.Housing),
            new(MushCustomizationIds.FurnitureDogBed, "개 침대", MushItemCategory.Housing, MushEquipmentSlot.Housing),
        };

        public static MushCustomizationItemDefinition Find(string itemId)
        {
            foreach (MushCustomizationItemDefinition item in Items)
            {
                if (item.id == itemId)
                    return item;
            }
            return null;
        }
    }

    [Serializable]
    public sealed class MushCustomizationState
    {
        public List<string> ownedItems = new();
        public string equippedSledBody = MushCustomizationIds.SledNatural;
        public string equippedSledDecoration = string.Empty;
        public string dogOneHat = string.Empty;
        public string dogOneNeck = string.Empty;
        public string dogTwoHat = string.Empty;
        public string dogTwoNeck = string.Empty;
        public bool furnitureTablePlaced;
        public bool furnitureChairPlaced;
        public bool furnitureDogBedPlaced;
        public int housingSaveVersion;
        public string housingChairItem = string.Empty;
        public string housingTableItem = string.Empty;
        public string housingDogRestItem = MushCustomizationIds.HousingDefaultDogCare;

        public bool Owns(string itemId)
        {
            return !string.IsNullOrEmpty(itemId) && ownedItems != null && ownedItems.Contains(itemId);
        }

        public bool Acquire(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return false;
            ownedItems ??= new List<string>();
            if (ownedItems.Contains(itemId))
                return false;
            ownedItems.Add(itemId);
            return true;
        }

        public string GetDogHat(int dogIndex) => dogIndex == 0 ? dogOneHat : dogTwoHat;
        public string GetDogNeck(int dogIndex) => dogIndex == 0 ? dogOneNeck : dogTwoNeck;

        public void SetDogHat(int dogIndex, string itemId)
        {
            if (dogIndex == 0) dogOneHat = itemId ?? string.Empty;
            else dogTwoHat = itemId ?? string.Empty;
        }

        public void SetDogNeck(int dogIndex, string itemId)
        {
            if (dogIndex == 0) dogOneNeck = itemId ?? string.Empty;
            else dogTwoNeck = itemId ?? string.Empty;
        }

        public bool GetHousingPlaced(int index)
        {
            return index switch
            {
                0 => furnitureTablePlaced,
                1 => furnitureChairPlaced,
                2 => furnitureDogBedPlaced,
                _ => false,
            };
        }

        public void SetHousingPlaced(int index, bool placed)
        {
            if (index == 0) furnitureTablePlaced = placed;
            else if (index == 1) furnitureChairPlaced = placed;
            else if (index == 2) furnitureDogBedPlaced = placed;
        }

        public string GetHousingPlacement(int placementIndex)
        {
            return placementIndex switch
            {
                0 => housingChairItem ?? string.Empty,
                1 => housingTableItem ?? string.Empty,
                2 => housingDogRestItem ?? string.Empty,
                _ => string.Empty,
            };
        }

        public void SetHousingPlacement(int placementIndex, string itemId)
        {
            itemId ??= string.Empty;
            if (placementIndex == 0) housingChairItem = itemId;
            else if (placementIndex == 1) housingTableItem = itemId;
            else if (placementIndex == 2) housingDogRestItem = itemId;
            housingSaveVersion = 1;
            SyncLegacyHousingFlags();
        }

        public void Normalize()
        {
            ownedItems ??= new List<string>();
            if (!ownedItems.Contains(MushCustomizationIds.SledNatural))
                ownedItems.Add(MushCustomizationIds.SledNatural);

            if (housingSaveVersion <= 0)
            {
                // Migrate the old three booleans once. Old slot order was
                // table, chair, dog bed; the new UI stores the actual item at
                // each natural lobby placement instead.
                housingChairItem = furnitureChairPlaced && Owns(MushCustomizationIds.FurnitureChair)
                    ? MushCustomizationIds.FurnitureChair
                    : string.Empty;
                housingTableItem = furnitureTablePlaced && Owns(MushCustomizationIds.FurnitureTable)
                    ? MushCustomizationIds.FurnitureTable
                    : string.Empty;
                housingDogRestItem = furnitureDogBedPlaced && Owns(MushCustomizationIds.FurnitureDogBed)
                    ? MushCustomizationIds.FurnitureDogBed
                    : MushCustomizationIds.HousingDefaultDogCare;
                housingSaveVersion = 1;
            }

            housingChairItem ??= string.Empty;
            housingTableItem ??= string.Empty;
            housingDogRestItem ??= MushCustomizationIds.HousingDefaultDogCare;
            if (housingChairItem != MushCustomizationIds.FurnitureChair || !Owns(housingChairItem))
                housingChairItem = string.Empty;
            if (housingTableItem != MushCustomizationIds.FurnitureTable || !Owns(housingTableItem))
                housingTableItem = string.Empty;
            if (housingDogRestItem != MushCustomizationIds.HousingDefaultDogCare &&
                (housingDogRestItem != MushCustomizationIds.FurnitureDogBed || !Owns(housingDogRestItem)))
                housingDogRestItem = string.Empty;
            SyncLegacyHousingFlags();

            if (string.IsNullOrEmpty(equippedSledBody) || !Owns(equippedSledBody))
                equippedSledBody = MushCustomizationIds.SledNatural;
            if (!string.IsNullOrEmpty(equippedSledDecoration) && !Owns(equippedSledDecoration))
                equippedSledDecoration = string.Empty;
            if (!string.IsNullOrEmpty(dogOneHat) && !Owns(dogOneHat)) dogOneHat = string.Empty;
            if (!string.IsNullOrEmpty(dogOneNeck) && !Owns(dogOneNeck)) dogOneNeck = string.Empty;
            if (!string.IsNullOrEmpty(dogTwoHat) && !Owns(dogTwoHat)) dogTwoHat = string.Empty;
            if (!string.IsNullOrEmpty(dogTwoNeck) && !Owns(dogTwoNeck)) dogTwoNeck = string.Empty;
        }

        private void SyncLegacyHousingFlags()
        {
            furnitureChairPlaced = housingChairItem == MushCustomizationIds.FurnitureChair;
            furnitureTablePlaced = housingTableItem == MushCustomizationIds.FurnitureTable;
            furnitureDogBedPlaced = housingDogRestItem == MushCustomizationIds.FurnitureDogBed;
        }

        public MushCustomizationState Clone()
        {
            MushCustomizationState copy = JsonUtility.FromJson<MushCustomizationState>(JsonUtility.ToJson(this));
            copy ??= new MushCustomizationState();
            copy.Normalize();
            return copy;
        }
    }

    public static class MushCustomizationSave
    {
        private const string SaveKey = "Mush.Customization.V1";

        public static MushCustomizationState Load()
        {
            MushCustomizationState state = null;
            string json = PlayerPrefs.GetString(SaveKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    state = JsonUtility.FromJson<MushCustomizationState>(json);
                }
                catch (Exception)
                {
                    state = null;
                }
            }

            state ??= new MushCustomizationState();
            state.Normalize();
            return state;
        }

        public static void Save(MushCustomizationState state)
        {
            if (state == null)
                return;
            state.Normalize();
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(state));
            PlayerPrefs.Save();
        }

        public static MushCustomizationState Reset()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            return Load();
        }
    }

    /// <summary>
    /// Development reset available from every playable scene. R clears only
    /// store ownership/equipment/housing customization and reloads the current
    /// scene so the reset is immediately visible.
    /// </summary>
    internal sealed class MushCustomizationResetHotkey : MonoBehaviour
    {
        private bool resetting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<MushCustomizationResetHotkey>() != null)
                return;

            GameObject resetObject = new("Mush Customization Reset Hotkey");
            DontDestroyOnLoad(resetObject);
            resetObject.AddComponent<MushCustomizationResetHotkey>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (resetting || keyboard == null || !keyboard.rKey.wasPressedThisFrame)
                return;

            resetting = true;
            MushCustomizationSave.Reset();
            Debug.Log("[Mush] 커스터마이징과 상점 보유 목록을 기본 상태로 초기화했습니다.", this);

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && !string.IsNullOrEmpty(activeScene.name))
                SceneManager.LoadScene(activeScene.name);
            else
                resetting = false;
        }
    }
}
