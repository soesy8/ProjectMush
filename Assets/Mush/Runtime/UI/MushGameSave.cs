using System;
using System.Collections.Generic;
using System.IO;
using Mush.Customization;
using UnityEngine;

public enum MushDogCondition
{
    Normal = 0,
    Bad = 1,
    Good = 2,
}

/// <summary>A single resumable game slot; audio preferences are independent of a new game.</summary>
public static class MushGameSave
{
    public const int TeamDogCount = 2;

    [Serializable]
    public sealed class DogState
    {
        public string dogId;
        public float stamina = 100f;
        public MushDogCondition condition = MushDogCondition.Normal;

        public DogState() { }

        public DogState(string id)
        {
            dogId = id;
        }
    }

    [Serializable]
    public sealed class Data
    {
        public int version = 1;
        public string scene = "PM_Lobby";
        public float stamina = 100f;
        public MushDogCondition dogCondition = MushDogCondition.Normal;
        public int dogStaminaVersion = 1;
        public List<DogState> dogs = new() { new DogState("dog_1"), new DogState("dog_2") };
        public int gold = 150;
        public int unlockedStageCount = 1;
        public bool riding;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public float elapsed;
        public bool timerStarted;
        public bool offCourse;
        public Mush.Prototype.MushSledKeyboardController.SavedMotion motion;
        public MushCustomizationState customization;
        public float[] bestTimes = { -1f, -1f, -1f };
        public int[] stars = new int[3];
    }

    private static readonly string[] Maps = { "snow", "Tree", "SharpCurve" };
    private static Data current;
    private static bool restoringRide;
    public static Data Current
    {
        get
        {
            if (current == null)
            {
                current = Read() ?? new Data();
                EnsureDogStates(current);
            }
            return current;
        }
    }
    public static float TeamStamina => CalculateTeamStamina(Current);
    public static MushDogCondition DogCondition => CalculateTeamCondition(Current);
    private static string PathName => Path.Combine(Application.persistentDataPath, "mush-save.json");
    public static bool HasSave => Read() != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { current = null; restoringRide = false; }

    private static Data Read()
    {
        foreach (string path in new[] { PathName, PathName + ".bak" })
        {
            try
            {
                if (!File.Exists(path)) continue;
                string json = File.ReadAllText(path);
                Data data = JsonUtility.FromJson<Data>(json);
                if (data != null && !json.Contains("\"dogStaminaVersion\"", StringComparison.Ordinal))
                    data.dogStaminaVersion = 0;
                if (data != null && data.scene == "MushLobby")
                    data.scene = "PM_Lobby"; // 기존 저장 파일은 새 아트 로비로 자연스럽게 이관한다.
                if (data == null || data.version != 1 || !KnownScene(data.scene) ||
                    !float.IsFinite(data.stamina) || !float.IsFinite(data.elapsed) || data.elapsed < 0f ||
                    data.bestTimes == null || data.bestTimes.Length != 3 || data.stars == null || data.stars.Length != 3)
                    continue;
                if (data.riding && (data.motion == null || !float.IsFinite(data.motion.speed) ||
                    !Finite(data.position) || !float.IsFinite(data.rotation.x) || !float.IsFinite(data.rotation.y) ||
                    !float.IsFinite(data.rotation.z) || !float.IsFinite(data.rotation.w))) continue;
                data.stamina = Mathf.Clamp(data.stamina, 0f, 100f);
                data.gold = Mathf.Max(0, data.gold);
                data.unlockedStageCount = ResolveUnlockedStageCount(data);
                if (data.stamina <= 0f) data.dogCondition = MushDogCondition.Bad;
                else if (data.dogCondition != MushDogCondition.Bad && data.dogCondition != MushDogCondition.Good)
                    data.dogCondition = MushDogCondition.Normal;
                EnsureDogStates(data);
                return data;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException) { }
        }
        return null;
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    private static bool KnownScene(string scene) => scene is "PM_Lobby" or "MushLobby" or "MushStore" or "MushHousing" or "snow" or "Tree" or "SharpCurve";

    public static string DogId(int dogIndex) => $"dog_{dogIndex + 1}";

    public static float GetDogStamina(int dogIndex)
    {
        DogState dog = GetDogState(Current, dogIndex);
        return dog != null ? dog.stamina : 0f;
    }

    public static MushDogCondition GetDogCondition(int dogIndex)
    {
        DogState dog = GetDogState(Current, dogIndex);
        return dog != null ? dog.condition : MushDogCondition.Normal;
    }

    private static DogState GetDogState(Data data, int dogIndex)
    {
        if (data == null || dogIndex < 0 || dogIndex >= TeamDogCount)
            return null;
        if (data.dogs == null || data.dogStaminaVersion < 1)
            EnsureDogStates(data);
        string id = DogId(dogIndex);
        return FindDogState(data, id);
    }

    private static DogState FindDogState(Data data, string dogId)
    {
        if (data?.dogs == null)
            return null;
        foreach (DogState dog in data.dogs)
            if (dog != null && dog.dogId == dogId)
                return dog;
        return null;
    }

    private static void EnsureDogStates(Data data)
    {
        if (data == null)
            return;

        float legacyStamina = float.IsFinite(data.stamina) ? Mathf.Clamp(data.stamina, 0f, 100f) : 100f;
        MushDogCondition legacyCondition = data.dogCondition is MushDogCondition.Bad or MushDogCondition.Good
            ? data.dogCondition : MushDogCondition.Normal;
        bool migrateLegacyState = data.dogStaminaVersion < 1;
        data.dogs ??= new List<DogState>();

        for (int index = 0; index < TeamDogCount; index++)
        {
            string id = DogId(index);
            DogState dog = FindDogState(data, id);
            if (dog == null)
            {
                dog = new DogState(id);
                data.dogs.Add(dog);
            }
            if (migrateLegacyState)
            {
                dog.stamina = legacyStamina;
                dog.condition = legacyCondition;
            }
            dog.stamina = float.IsFinite(dog.stamina) ? Mathf.Clamp(dog.stamina, 0f, 100f) : legacyStamina;
            if (dog.stamina <= 0f)
                dog.condition = MushDogCondition.Bad;
            else if (dog.condition != MushDogCondition.Bad && dog.condition != MushDogCondition.Good)
                dog.condition = MushDogCondition.Normal;
        }

        data.dogStaminaVersion = 1;
        SyncLegacyTeamState(data);
    }

    private static float CalculateTeamStamina(Data data)
    {
        if (data == null)
            return 100f;
        float sum = 0f;
        int count = 0;
        for (int index = 0; index < TeamDogCount; index++)
        {
            DogState dog = FindDogState(data, DogId(index));
            if (dog == null)
                continue;
            sum += Mathf.Clamp(dog.stamina, 0f, 100f);
            count++;
        }
        return count > 0 ? sum / count : 100f;
    }

    private static MushDogCondition CalculateTeamCondition(Data data)
    {
        bool allGood = true;
        for (int index = 0; index < TeamDogCount; index++)
        {
            DogState dog = FindDogState(data, DogId(index));
            if (dog == null)
            {
                allGood = false;
                continue;
            }
            if (dog.stamina <= 0f || dog.condition == MushDogCondition.Bad)
                return MushDogCondition.Bad;
            if (dog.condition != MushDogCondition.Good)
                allGood = false;
        }
        return allGood ? MushDogCondition.Good : MushDogCondition.Normal;
    }

    private static void SyncLegacyTeamState(Data data)
    {
        data.stamina = CalculateTeamStamina(data);
        data.dogCondition = CalculateTeamCondition(data);
    }

    public static bool IsStageUnlocked(string sceneName)
    {
        int stageIndex = Array.IndexOf(Maps, sceneName);
        return stageIndex < 0 || stageIndex < ResolveUnlockedStageCount(Current);
    }

    public static int StageReward(string sceneName)
    {
        return Array.IndexOf(Maps, sceneName) switch
        {
            0 => 100,
            1 => 150,
            2 => 200,
            _ => 0,
        };
    }

    public static int AwardStageCompletion(string sceneName, out bool unlockedNextStage)
    {
        Data data = Current;
        int stageIndex = Array.IndexOf(Maps, sceneName);
        int previousUnlockedCount = ResolveUnlockedStageCount(data);
        int reward = StageReward(sceneName);
        data.gold = (int)Math.Min(int.MaxValue, (long)data.gold + reward);
        if (stageIndex >= 0)
            data.unlockedStageCount = Mathf.Clamp(Mathf.Max(previousUnlockedCount, stageIndex + 2), 1, Maps.Length);
        unlockedNextStage = data.unlockedStageCount > previousUnlockedCount;
        return reward;
    }

    public static bool TrySpendGold(int amount)
    {
        amount = Mathf.Max(0, amount);
        if (Current.gold < amount)
            return false;
        Current.gold -= amount;
        Save();
        return true;
    }

    private static int ResolveUnlockedStageCount(Data data)
    {
        int unlocked = Mathf.Clamp(data != null ? data.unlockedStageCount : 1, 1, Maps.Length);
        if (data?.stars != null)
        {
            if (data.stars.Length > 0 && data.stars[0] > 0) unlocked = Mathf.Max(unlocked, 2);
            if (data.stars.Length > 1 && data.stars[1] > 0) unlocked = Mathf.Max(unlocked, 3);
        }
        if (MushMapRecords.GetBestStars(Maps[0]) > 0) unlocked = Mathf.Max(unlocked, 2);
        if (MushMapRecords.GetBestStars(Maps[1]) > 0) unlocked = Mathf.Max(unlocked, 3);
        return Mathf.Clamp(unlocked, 1, Maps.Length);
    }

    public static void NewGame()
    {
        Mush.Lobby.MushLobbyDogRoamer.ClearSavedLobbyPositions();
        current = new Data();
        restoringRide = false;
        MushCustomizationSave.Reset();
        foreach (string map in Maps)
        {
            PlayerPrefs.DeleteKey("Mush.BestCompletionSeconds." + map);
            PlayerPrefs.DeleteKey("Mush.BestStars." + map);
        }
        Save();
    }

    public static string ContinueGame()
    {
        Data saved = Read();
        if (saved == null) return null;
        current = saved;
        if (saved.customization != null) MushCustomizationSave.Save(saved.customization);
        for (int i = 0; i < Maps.Length; i++)
        {
            PlayerPrefs.DeleteKey("Mush.BestCompletionSeconds." + Maps[i]);
            PlayerPrefs.DeleteKey("Mush.BestStars." + Maps[i]);
            if (saved.bestTimes[i] >= 0f)
                MushMapRecords.SaveCompletion(Maps[i], saved.bestTimes[i], saved.stars[i]);
        }
        PlayerPrefs.Save();
        restoringRide = saved.riding;
        return saved.scene;
    }

    public static bool ConsumeRideRestore(string scene)
    {
        if (!restoringRide || Current.scene != scene) return false;
        restoringRide = false;
        return true;
    }

    public static void EnterLobby()
    {
        Data data = Current;
        for (int index = 0; index < TeamDogCount; index++)
        {
            DogState dog = GetDogState(data, index);
            if (dog != null) dog.stamina = Mathf.Floor(dog.stamina);
        }
        SyncLegacyTeamState(data);
        data.riding = false;
        data.scene = "PM_Lobby";
        Save();
    }

    public static void RestoreStamina(int amount)
    {
        for (int index = 0; index < TeamDogCount; index++)
            RestoreDogStaminaInternal(Current, index, amount);
        SyncLegacyTeamState(Current);
        Save();
    }

    public static void RestoreDogStamina(int dogIndex, int amount)
    {
        RestoreDogStaminaInternal(Current, dogIndex, amount);
        SyncLegacyTeamState(Current);
        Save();
    }

    private static void RestoreDogStaminaInternal(Data data, int dogIndex, int amount)
    {
        DogState dog = GetDogState(data, dogIndex);
        if (dog == null)
            return;
        dog.stamina = Mathf.Clamp(Mathf.FloorToInt(dog.stamina) + amount, 0, 100);
        if (dog.stamina >= 100f)
            dog.condition = MushDogCondition.Normal;
    }

    public static void ConsumeStamina(float amount)
    {
        Data data = Current;
        float cost = Mathf.Max(0f, amount);
        for (int index = 0; index < TeamDogCount; index++)
        {
            DogState dog = GetDogState(data, index);
            if (dog == null)
                continue;
            dog.stamina = Mathf.Max(0f, dog.stamina - cost);
            if (dog.stamina <= 0f)
                dog.condition = MushDogCondition.Bad;
        }
        SyncLegacyTeamState(data);
    }

    public static void PetDog()
    {
        for (int index = 0; index < TeamDogCount; index++)
            PetDogInternal(Current, index);
        SyncLegacyTeamState(Current);
        Save();
    }

    public static void PetDog(int dogIndex)
    {
        if (!PetDogInternal(Current, dogIndex))
            return;
        SyncLegacyTeamState(Current);
        Save();
    }

    private static bool PetDogInternal(Data data, int dogIndex)
    {
        DogState dog = GetDogState(data, dogIndex);
        if (dog == null || dog.stamina < 100f || dog.condition == MushDogCondition.Good)
            return false;
        dog.condition = MushDogCondition.Good;
        return true;
    }

    public static bool Save()
    {
        Data data = Current;
        EnsureDogStates(data);
        SyncLegacyTeamState(data);
        data.gold = Mathf.Max(0, data.gold);
        data.unlockedStageCount = ResolveUnlockedStageCount(data);
        if (data.stamina <= 0f) data.dogCondition = MushDogCondition.Bad;
        data.customization = MushCustomizationSave.Load();
        for (int i = 0; i < Maps.Length; i++)
        {
            data.bestTimes[i] = MushMapRecords.TryGetBestTime(Maps[i], out float seconds) ? seconds : -1f;
            data.stars[i] = MushMapRecords.GetBestStars(Maps[i]);
        }
        try
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            string temporary = PathName + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(PathName)) File.Replace(temporary, PathName, PathName + ".bak");
            else File.Move(temporary, PathName);
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogError($"[Mush] 게임 저장에 실패했습니다: {exception.Message}");
            return false;
        }
    }
}
