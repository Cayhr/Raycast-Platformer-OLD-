using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HB_PlayerSpearSwing : HitboxFramework
{
    [SerializeField] private PlayerController _PC;
    [SerializeField] private EntityController _EC;

    private int playerStats;

    public override void OnHit(EntityController en)
    {
        // Apply knockback.
        en.ApplyVelocity(_EC.directionalInfluence * 5f);
    }
}
