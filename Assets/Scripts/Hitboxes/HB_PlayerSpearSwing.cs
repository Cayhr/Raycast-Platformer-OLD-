using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HB_PlayerSpearSwing : HitboxBase
{
    [SerializeField] private PlayerController _PC;
    [SerializeField] private EntityController _EC;

    private int playerStats;
    private int hits = 0;

    private void Awake()
    {
        hits = 0;        
    }

    private void OnEnable()
    {
        hits = 0;
    }

    private void OnDisable()
    {
        
    }


    public override void OnHit(EntityController en)
    {
        hits++;
        OnFirstHit(hits);
        // Apply knockback.
        const float airKB = 50f;
        float knockback = 15f;
        float personalKB = 5f;
        en.ApplyVelocity(_PC.lastSwingDirection, knockback);
        if (_EC.state == EntityMotionState.AIR && _PC.lastSwingDirection.y < 0f)
        {
            personalKB = airKB;
            _EC.TallyAirTime();
        }
        _EC.ApplyVelocity(_PC.lastSwingDirection, -personalKB);
    }

    /*
     * For any logic regarding the first hit dealt by this hitbox.
     */
    private void OnFirstHit(int numHit)
    {
        if (numHit > 1) return;

        // Heat reduction
        if (_PC.currentHeat > 0f && _PC.overheating)
        {
            float consumedHeat = (_PC.currentHeat > _PC.heatConsumption) ? _PC.heatConsumption : _PC.currentHeat;
            _PC.AdjustCurrentHeat(-consumedHeat);

            // We always have a delay before heat decays.
            _PC.heatDelayCounter = _PC.heatDecayDelay;
        }
    }
}
