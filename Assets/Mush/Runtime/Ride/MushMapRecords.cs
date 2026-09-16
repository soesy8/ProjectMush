using UnityEngine;

/// <summary>Map selection and delivery results share the same saved progress.</summary>
public static class MushMapRecords
{
    private const string TimePrefix = "Mush.BestCompletionSeconds.";
    private const string StarsPrefix = "Mush.BestStars.";

    public static bool TryGetBestTime(string sceneName, out float seconds)
    {
        seconds = PlayerPrefs.GetFloat(TimePrefix + sceneName, -1f);
        return seconds >= 0f && seconds < float.MaxValue && !float.IsInfinity(seconds);
    }

    public static int GetBestStars(string sceneName)
    {
        if (!TryGetBestTime(sceneName, out float bestSeconds))
            return 0;
        if (PlayerPrefs.HasKey(StarsPrefix + sceneName))
            return Mathf.Clamp(PlayerPrefs.GetInt(StarsPrefix + sceneName), 1, 3);

        // Earlier versions saved only the time. All three existing maps used 120 s / 80%.
        return CalculateStars(bestSeconds, 120f, 0.8f);
    }

    public static int CalculateStars(float seconds, float timeLimit, float threeStarRatio)
    {
        return seconds <= timeLimit * threeStarRatio ? 3 : seconds <= timeLimit ? 2 : 1;
    }

    public static float SaveCompletion(string sceneName, float seconds, int stars)
    {
        if (string.IsNullOrEmpty(sceneName) || seconds < 0f || float.IsNaN(seconds) || float.IsInfinity(seconds))
            return TryGetBestTime(sceneName, out float saved) ? saved : 0f;

        int previousStars = GetBestStars(sceneName);
        float best = TryGetBestTime(sceneName, out float previousTime) ? Mathf.Min(previousTime, seconds) : seconds;
        PlayerPrefs.SetFloat(TimePrefix + sceneName, best);
        PlayerPrefs.SetInt(StarsPrefix + sceneName, Mathf.Max(previousStars, Mathf.Clamp(stars, 1, 3)));
        PlayerPrefs.Save();
        return best;
    }

    public static string BestTimeLabel(string sceneName)
    {
        if (!TryGetBestTime(sceneName, out float seconds))
            return "최고기록\n기록 없음";
        int totalSeconds = Mathf.FloorToInt(seconds);
        return $"최고기록\n{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }
}
