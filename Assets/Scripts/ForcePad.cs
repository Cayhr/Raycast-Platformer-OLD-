using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ForcePad : MonoBehaviour
{

    [SerializeField] private float pushForce;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // First check if the collision was against a game object.
        if (collision.gameObject == null) return;

        // Did we get an entity?
        EntityController entity = collision.gameObject.GetComponent<EntityController>();
        PlayerController player = collision.gameObject.GetComponent<PlayerController>();
        
        // We HAVE to have an Entity.
        if (entity == null) return;

        // If we hit the player, then we can do this.
        if (player != null)
        {
            // EndDash() also calls TallyAirTime(), so it will be properly reset.
            // Redundant if the player is not doing a Gravity Suspension Dash.
            player.EndDash();
            player.SetJumps(player.maxJumps - 1);
        }

        entity.ApplyVelocity(transform.up, pushForce);
    }
}
