﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerUIHeatEffect : MonoBehaviour
{
    [SerializeField] private Slider heatSlider;
    [SerializeField] private RectTransform heatEffect;
    [SerializeField] private Image heatImage, handleImage;
    private float targetHeatYScale, targetHeat, maxHeat;
    private const float overheatYScale = 1f;
    private const float normalYScale = .5f;
    //private Color criticalHandle = new Color();
    private Color normalHandle = Color.white;

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
        handleImage.color = PlayerController.heatColor;
    }

    public void OverheatOff()
    {
        targetHeatYScale = normalYScale;
        handleImage.color = normalHandle;
    }

    public void TargetValue(float heat) => targetHeat = heat;

    public void SetMax(float max)
    {
        heatSlider.maxValue = max;
        maxHeat = max;
    }
}
