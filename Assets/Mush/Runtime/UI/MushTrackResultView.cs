using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>Updates the authored result panel from the completed ride's values.</summary>
[DisallowMultipleComponent]
public sealed class MushTrackResultView : MonoBehaviour
{
    [SerializeField] private TMP_Text currentTimeText;
    [SerializeField] private TMP_Text bestTimeText;
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text goldText;
    [Tooltip("Criteria and highlights are ordered from three stars to one star.")]
    [SerializeField] private TMP_Text[] rankCriteriaTexts = new TMP_Text[3];
    [SerializeField] private GameObject[] rankHighlights = new GameObject[3];
    [SerializeField] private Transform[] starSlots = new Transform[3];

    public void ShowResults(float elapsedSeconds, float bestSeconds, int stars, int awardedGold,
        float timeLimitSeconds, float threeStarTimeRatio)
    {
        currentTimeText.text = FormatTime(elapsedSeconds);
        bestTimeText.text = FormatTime(bestSeconds);
        rankText.text = $"별 {stars}개";
        goldText.text = $"+{awardedGold.ToString("N0", CultureInfo.InvariantCulture)} G";

        // Use the same thresholds as MushMapRecords.CalculateStars, including fractional seconds.
        string threeStarLimit = FormatThreshold(timeLimitSeconds * threeStarTimeRatio);
        string twoStarLimit = FormatThreshold(timeLimitSeconds);
        rankCriteriaTexts[0].text = $"{threeStarLimit} 이하";
        rankCriteriaTexts[1].text = $"{threeStarLimit} 초과\n{twoStarLimit} 이하";
        rankCriteriaTexts[2].text = $"{twoStarLimit} 초과";
        for (int index = 0; index < rankHighlights.Length; index++)
            rankHighlights[index].SetActive(stars == 3 - index);
        foreach (Transform slot in starSlots)
            slot.gameObject.SetActive(true);
    }

    public Vector3 StarTarget(int index) => transform.InverseTransformPoint(starSlots[index].position);
    public Vector3 StarScale(int index) => starSlots[index].localScale;
    public void LandStar(int index) => starSlots[index].gameObject.SetActive(false);

    private static string FormatTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private static string FormatThreshold(float seconds)
    {
        int milliseconds = Mathf.Max(0, Mathf.RoundToInt(seconds * 1000f));
        int totalSeconds = milliseconds / 1000;
        string time = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        int fraction = milliseconds % 1000;
        return fraction == 0 ? time : time + "." + fraction.ToString("000", CultureInfo.InvariantCulture).TrimEnd('0');
    }
}
