using UnityEngine;

/// <summary>A scene-owned team member. Duplicate this object to extend the sled team.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Mush/Ride Dog")]
public sealed class MushRideDog : MonoBehaviour
{
    [SerializeField] private Transform visual;
    [SerializeField] private Transform harness;
    [SerializeField, Min(-1), Tooltip("저장 장비 번호입니다. -1은 씬에 배치한 장식을 그대로 사용합니다.")]
    private int customizationIndex = -1;
    [SerializeField] private bool useMalamuteAccessories;
    [SerializeField, Range(0f, 6.283185f)] private float gaitPhase;
    [SerializeField, Tooltip("다른 개의 하네스 또는 썰매 연결점입니다.")] private Transform towFrom;
    [SerializeField] private LineRenderer towLine;

    public Transform Visual => visual;
    public Transform Harness => harness;
    public int CustomizationIndex => customizationIndex;
    public bool UseMalamuteAccessories => useMalamuteAccessories;
    public float GaitPhase => gaitPhase;

    public void Configure(Transform model, Transform anchor, int equipmentIndex, bool malamute, float phase)
    {
        visual = model;
        harness = anchor;
        customizationIndex = equipmentIndex;
        useMalamuteAccessories = malamute;
        gaitPhase = phase;
    }

    private void LateUpdate()
    {
        if (towLine == null || towFrom == null || harness == null)
            return;
        towLine.useWorldSpace = true;
        if (towLine.positionCount != 3)
            towLine.positionCount = 3;
        towLine.SetPosition(0, towFrom.position);
        towLine.SetPosition(1, (towFrom.position + harness.position) * 0.5f + Vector3.down * 0.12f);
        towLine.SetPosition(2, harness.position);
    }
}
