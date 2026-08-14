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
                ChairPlacement => new Vector3(-2.40f, 0f, -4.45f), // 의자는 플레이어 옆이 아니라 정면 깊은 생활 구역으로 올려 좌식 VR에서 고개를 옆으로 심하게 돌리지 않아도 보이게 한다.
                TablePlacement => new Vector3(-1.18f, 0f, -4.45f), // 탁자는 의자에서 약 1.2m 옆에 붙여 한 가구 코너처럼 보이게 하되 개 중앙 통로는 비워 둔다.
                DogRestPlacement => new Vector3(2.30f, 0f, -4.45f), // 개 침대는 반대편 정면 생활 구역에 두어 양옆 사각지대를 비우면서도 앉은 자리에서 자는 모습을 볼 수 있게 한다.
                _ => Vector3.zero,
            };
        }

        public static Quaternion Rotation(int placementIndex)
        {
            float yaw = placementIndex switch
            {
                ChairPlacement => 20f, // 의자는 옆벽이 아니라 방 중앙과 탁자 쪽을 바라보게 살짝만 안쪽으로 돌린다.
                TablePlacement => 0f, // 탁자는 정면 축을 유지해 의자 옆에 붙은 한 세트처럼 안정적으로 보이게 한다.
                DogRestPlacement => -15f, // 개 침대는 중앙 생활 공간 쪽으로 살짝 돌려 누운 개의 몸이 플레이어 시야에 잘 들어오게 한다.
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
        public string housingDogRestItem = string.Empty; // 개 침대 슬롯은 실제 가구만 저장하며 옛 "기본 개 돌보기" 가상 항목은 더 이상 기본값으로 사용하지 않는다.

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
                    : string.Empty; // 구형 저장의 개 침대가 없었다면 세 번째 슬롯은 그냥 빈 상태로 이관한다.
                housingSaveVersion = 1;
            }

            housingChairItem ??= string.Empty;
            housingTableItem ??= string.Empty;
            housingDogRestItem ??= string.Empty; // 세 번째 슬롯의 null 값도 가상 기본 아이템이 아니라 빈 슬롯으로 정규화한다.
            if (housingDogRestItem == MushCustomizationIds.HousingDefaultDogCare)
                housingDogRestItem = string.Empty; // 이전 버전에서 저장된 "기본 개 돌보기" 값은 로드 즉시 제거해 밥그릇과 하우징 슬롯을 분리한다.
            if (housingChairItem != MushCustomizationIds.FurnitureChair || !Owns(housingChairItem))
                housingChairItem = string.Empty;
            if (housingTableItem != MushCustomizationIds.FurnitureTable || !Owns(housingTableItem))
                housingTableItem = string.Empty;
            if (housingDogRestItem != MushCustomizationIds.FurnitureDogBed || !Owns(housingDogRestItem))
                housingDogRestItem = string.Empty; // 세 번째 슬롯에는 실제로 보유한 개 침대 모델 외의 값이 남지 않게 한다.
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

            // Ride maps reserve plain R for returning the complete sled team
            // to its last safe course checkpoint. Keep the prototype data
            // reset shortcut available in lobby/customization scenes only.
            if (FindFirstObjectByType<MushMapRideBootstrap>() != null)
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
