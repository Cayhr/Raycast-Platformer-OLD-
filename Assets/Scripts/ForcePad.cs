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
        PlayerController entity = collision.gameObject.GetComponent<PlayerController>();
        if (entity == null) return;

        entity.SetJumps(entity.maxJumps - 1);

        // EndDash() also calls TallyAirTime(), so it will be properly reset.
        // Redundant if the entity is not doing a Gravity Suspension Dash.
        entity.EndDash();
        entity.ApplyVelocity(transform.up, pushForce);
    }
}
