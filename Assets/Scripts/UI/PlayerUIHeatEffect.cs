using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerUIHeatEffect : MonoBehaviour
{
    [SerializeField] private Slider heatSlider;
    [SerializeField] private RectTransform heatEffect;
    [SerializeField] private Image heatImage;
    private float targetHeatYScale, targetHeat, maxHeat;
    private const float overheatYScale = 1f;
    private const float normalYScale = .5f;

    private void Start()
    {
        OverheatOff();
    }

    // Update is called once per frame
    void Update()
    {
        heatSlider.value = Mathf.Lerp(heatSlider.value, targetHeat, 0.5f);
        heatImage.fillAmount = Mathf.Lerp(heatImage.fillAmount, targetHeat / maxHeat, 0.5f);
        heatEffect.localScale = new Vector2(1f, Mathf.Lerp(heatEffect.localScale.y, targetHeatYScale, 0.5f));
    }

    public void OverheatOn()
    {
        targetHeatYScale = overheatYScale;
    }

    public void OverheatOff()
    {
        targetHeatYScale = normalYScale;
    }

    public void TargetValue(float heat) => targetHeat = heat;

    public void SetMax(float max)
    {
        heatSlider.maxValue = max;
        maxHeat = max;
    }
}
