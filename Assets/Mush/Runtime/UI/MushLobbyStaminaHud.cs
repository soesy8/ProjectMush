using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MushLobbyStaminaHud : MonoBehaviour
{
    [SerializeField] private Image staminaFill;
    [SerializeField] private TMP_Text staminaText;
    [SerializeField] private MushDogConditionIcon conditionIcon;
    [SerializeField] private TMP_Text conditionText;
    private int displayedStamina = -1;
    private MushDogCondition? displayedCondition;

    private void OnEnable()
    {
        displayedStamina = -1;
        displayedCondition = null;
        Refresh();
    }

    private void LateUpdate() => Refresh();

    private void Refresh()
    {
        MushDogCondition condition = MushGameSave.DogCondition;
        if (condition != displayedCondition)
        {
            displayedCondition = condition;
            if (conditionIcon != null) conditionIcon.SetCondition(condition);
            if (conditionText != null)
                conditionText.text = condition switch
                {
                    MushDogCondition.Bad => "나쁨",
                    MushDogCondition.Good => "좋음",
                    _ => "평범",
                };
        }
        int stamina = Mathf.Clamp(Mathf.FloorToInt(MushGameSave.Current.stamina), 0, 100);
        if (stamina == displayedStamina)
            return;

        displayedStamina = stamina;
        if (staminaFill != null)
            staminaFill.fillAmount = stamina / 100f;
        if (staminaText != null)
            staminaText.SetText("썰매견 체력  {0} / 100", stamina);
    }
}
