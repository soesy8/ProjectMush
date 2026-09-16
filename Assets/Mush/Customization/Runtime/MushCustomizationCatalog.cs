using UnityEngine;

namespace Mush.Customization
{
    [CreateAssetMenu(fileName = "MushCustomizationCatalog", menuName = "Mush/Customization Catalog")]
    public sealed class MushCustomizationCatalog : ScriptableObject
    {
        [Header("UI Theme")]
        public Font koreanFont;
        public GameObject uiPanelPrefab;

        [Header("Housing Preview Environment")]
        public GameObject lobbyEnvironment;

        [Header("Sleds")]
        public GameObject sledNatural;
        public GameObject sledRed;
        public GameObject sledBlue;
        public GameObject sledBlack;
        public GameObject sledSanta;
        public GameObject sledFrontLantern;

        [Header("Dogs")]
        public GameObject husky;
        public GameObject malamute;
        public GameObject huskyFedora;
        public GameObject malamuteFedora;
        public GameObject huskySantaHat;
        public GameObject malamuteSantaHat;
        public GameObject huskyPurpleScarf;
        public GameObject malamutePurpleScarf;
        public GameObject huskyRedBandana;
        public GameObject malamuteRedBandana;

        [Header("Housing")]
        public GameObject furnitureTable;
        public GameObject furnitureChair;
        public GameObject furnitureDogBed;

        private static MushCustomizationCatalog cached;

        public static MushCustomizationCatalog Load()
        {
            if (cached == null)
                cached = Resources.Load<MushCustomizationCatalog>("MushCustomizationCatalog");
            return cached;
        }

        public GameObject GetPrefab(string itemId, bool malamuteDog = false)
        {
            return itemId switch
            {
                MushCustomizationIds.SledNatural => sledNatural,
                MushCustomizationIds.SledRed => sledRed,
                MushCustomizationIds.SledBlue => sledBlue,
                MushCustomizationIds.SledBlack => sledBlack,
                MushCustomizationIds.SledSanta => sledSanta,
                MushCustomizationIds.SledLantern => sledFrontLantern,
                MushCustomizationIds.DogFedora => malamuteDog ? malamuteFedora : huskyFedora,
                MushCustomizationIds.DogSantaHat => malamuteDog ? malamuteSantaHat : huskySantaHat,
                MushCustomizationIds.DogPurpleScarf => malamuteDog ? malamutePurpleScarf : huskyPurpleScarf,
                MushCustomizationIds.DogRedBandana => malamuteDog ? malamuteRedBandana : huskyRedBandana,
                MushCustomizationIds.FurnitureTable => furnitureTable,
                MushCustomizationIds.FurnitureChair => furnitureChair,
                MushCustomizationIds.FurnitureDogBed => furnitureDogBed,
                _ => null,
            };
        }
    }
}
