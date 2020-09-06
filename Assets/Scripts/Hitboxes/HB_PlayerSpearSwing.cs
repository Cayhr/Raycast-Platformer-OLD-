using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HB_PlayerSpearSwing : HitboxBase
{
    [SerializeField] private PlayerController _PC;
    [SerializeField] private EntityController _EC;

    private int playerStats;

    public override void OnHit(EntityController en)
    {
        if (_PC.currentHeat > 0f && _PC.overheating)
        {
            float consumedHeat = (_PC.currentHeat > _PC.heatConsumption) ? _PC.heatConsumption : _PC.currentHeat;
            _PC.AdjustCurrentHeat(-consumedHeat);

            // We always have a delay before heat decays.
            _PC.heatDelayCounter = _PC.heatDecayDelay;
        }
        // Apply knockback.
        en.ApplyVelocity(_PC.lastSwingDirection, 15f);
        Debug.Log("Hit : " + en.entityName);
    }
}
