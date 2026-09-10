using System;
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
    [Serializable]
    public sealed class Data
    {
        public int version = 1;
        public string scene = "MushLobby";
        public float stamina = 100f;
        public MushDogCondition dogCondition = MushDogCondition.Normal;
        public int gold = 150;
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
    public static Data Current => current ??= Read() ?? new Data();
    public static MushDogCondition DogCondition => Current.stamina <= 0f
        ? MushDogCondition.Bad : Current.dogCondition;
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
                Data data = JsonUtility.FromJson<Data>(File.ReadAllText(path));
                if (data == null || data.version != 1 || !KnownScene(data.scene) ||
                    !float.IsFinite(data.stamina) || !float.IsFinite(data.elapsed) || data.elapsed < 0f ||
                    data.bestTimes == null || data.bestTimes.Length != 3 || data.stars == null || data.stars.Length != 3)
                    continue;
                if (data.riding && (data.motion == null || !float.IsFinite(data.motion.speed) ||
                    !Finite(data.position) || !float.IsFinite(data.rotation.x) || !float.IsFinite(data.rotation.y) ||
                    !float.IsFinite(data.rotation.z) || !float.IsFinite(data.rotation.w))) continue;
                data.stamina = Mathf.Clamp(data.stamina, 0f, 100f);
                if (data.stamina <= 0f) data.dogCondition = MushDogCondition.Bad;
                else if (data.dogCondition != MushDogCondition.Bad && data.dogCondition != MushDogCondition.Good)
                    data.dogCondition = MushDogCondition.Normal;
                return data;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException) { }
        }
        return null;
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    private static bool KnownScene(string scene) => scene is "MushLobby" or "MushStore" or "MushHousing" or "snow" or "Tree" or "SharpCurve";

    public static void NewGame()
    {
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
        Current.stamina = Mathf.Floor(Current.stamina);
        Current.riding = false;
        Current.scene = "MushLobby";
        Save();
    }

    public static void RestoreStamina(int amount)
    {
        Current.stamina = Mathf.Clamp(Mathf.FloorToInt(Current.stamina) + amount, 0, 100);
        if (Current.stamina >= 100f)
            Current.dogCondition = MushDogCondition.Normal;
        Save();
    }

    public static void ConsumeStamina(float amount)
    {
        Current.stamina = Mathf.Max(0f, Current.stamina - Mathf.Max(0f, amount));
        if (Current.stamina <= 0f)
            Current.dogCondition = MushDogCondition.Bad;
    }

    public static void PetDog()
    {
        if (Current.stamina < 100f || Current.dogCondition == MushDogCondition.Good)
            return;
        Current.dogCondition = MushDogCondition.Good;
        Save();
    }

    public static bool Save()
    {
        Data data = Current;
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
