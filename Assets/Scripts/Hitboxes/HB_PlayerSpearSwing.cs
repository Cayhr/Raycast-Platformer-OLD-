using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HB_PlayerSpearSwing : HitboxFramework
{
    [SerializeField] private PlayerController _PC;

    private int playerStats;

    public override void OnHit(EntityController en)
    {
        en.ApplyVelocity(Vector3.right * 25f);
    }
}
