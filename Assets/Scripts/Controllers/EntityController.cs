using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public delegate Vector2 VelocityCompoundMethod();
public delegate void StateBasedAction();
public enum FactionList { NEUTRAL, PLAYER, ENEMIES};
public enum EntityMotionState { GROUNDED, AIR, CLUTCH };

public class EntityController : MonoBehaviour
{
    private Rigidbody2D rb;
    private static readonly float RAYCAST_INLET = 0.015f;
    //public CircleCollider2D mainCollider;
    //public BoxCollider2D headCollider;
    private VelocityCompoundMethod vOverride, vAdd, vMult;
    private StateBasedAction sbaOnLanding, sbaWhileInAir;

    [Header("References")]
    // The forms this Entity can take. At a minimum, are a GameObject with sprite and collider.
    // Different forms should only be changes in hitboxes, such as player standing and crouching form.
    [SerializeField] private List<GameObject> forms = new List<GameObject>();

    [Header("Entity Descriptors")]
    public string entityName;
    public int health, maxHealth;
    public FactionList faction;
    [SerializeField] private int formIndex;
    private GameObject formObject;
    private BoxCollider2D formCollider;

    [Header("Runtime Statistics")]
    public EntityMotionState state;
    public Vector2 currentVelocity = Vector2.zero;
    public Vector2 externalVelocity = Vector2.zero;
    public bool facingRight = true;

    [Header("Physics Parameters")]
    public Vector2 normalVector;
    public bool gravityOn;
    public float gravityCoeff, maxGravitySpeed, inertialDampening, dampeningCutoff;
    [SerializeField] int rayPrecisionBase, rayPrecisionHeight;    // The amount of extra points BETWEEN the 2 on the edges.

    [Header("Runtime Trackers")]
    public float subAirTime = 0;
    public float totalAirTime = 0;
    public Vector2 directionalInfluence = Vector2.zero;

    // Raycasting Variables
    private Bounds formBounds;
    private Vector2[] relativeBasePoints;
    private Vector2[] relativeHeightPoints;
    float directionMultiplier;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        //mainCollider = GetComponent<CircleCollider2D>();
        //headCollider = GetComponent<BoxCollider2D>();
        if (forms.Count == 0)
        {
            Debug.LogError(gameObject + " has no forms.");
            gameObject.SetActive(false);
        }
        ChangeForm(formIndex);
        if (rb == null)
        {
            Debug.LogError("Missing Rigidbody2D for " + gameObject);
        }
        else
        {
            Physics2D.IgnoreLayerCollision(8, 8, true);
        }
    }

    public bool FacingRight() => facingRight;

    public void SetVelocity(Vector2 ve) => currentVelocity = ve;

    /// <summary>
    /// Sets the velocity functions for the Entity.
    /// </summary>
    public void SetVelocityFunctions(VelocityCompoundMethod over, VelocityCompoundMethod add, VelocityCompoundMethod mult)
    {
        vOverride = over;
        vAdd = add;
        vMult = mult;
    }

    public void SetEventFunctions(StateBasedAction landing, StateBasedAction inAir)
    {
        sbaOnLanding = landing;
        sbaWhileInAir = inAir;
    }

    public void Update()
    {
        // Raycast up and down to check for floors.
        //RaycastHit2D floorCheck = Physics2D.CircleCast((Vector2)transform.position + mainCollider.offset, mainCollider.radius, normalVector * -1f, 0.05f, LayerInfo.TERRAIN);
        //RaycastHit2D ceilingCheck = Physics2D.CircleCast((Vector2)transform.position + headCollider.offset, 0.49f, Vector2.up, 0.05f, terrainLayer);

        if (directionalInfluence.x > 0) facingRight = true;
        else if (directionalInfluence.x < 0) facingRight = false;
        directionMultiplier = facingRight ? 1f : -1f;

        // First calculate the final velocity of the entity.
        currentVelocity = CompoundVelocities(vOverride, vAdd, vMult);

        // Do our Raycast Movement logic.
        RaycastMovement();


        DecayExternalVelocity();
    }

    private void RaycastMovement()
    {
        Vector2 scaledVelocity = currentVelocity * Time.deltaTime;

        // Perpendicular of inverse of the normal points in the positive X direction, or (1, 0) on unit circle.
        Vector2 moveX = Vector2.zero;
        int xHits = RaycastX(Vector2.Perpendicular(normalVector * -1f) * Mathf.Sign(currentVelocity.x), scaledVelocity.x, ref moveX);

        // Normal Vector points "up".
        Vector2 moveY = Vector2.zero;
        int yHits = RaycastY(normalVector * Mathf.Sign(currentVelocity.y), scaledVelocity.y, ref moveY);
        if (yHits > 0)
        {
            if (scaledVelocity.y < 0f)
            {
                state = EntityMotionState.GROUNDED;
                OnLanding();
            }
        }

        // We use Translate because Rigidbody2D.MovePosition() creates weird jittering situations. Translate is more precise!
        transform.Translate(moveX + moveY);

        // Sync after every transform translation.
        Physics2D.SyncTransforms();

        // If we are on the ground, we should check if we are still on it.
        // Slap the results of the ground checks in moveY, even though we don't use it anymore.
        int groundChecks = (state == EntityMotionState.GROUNDED) ? RaycastY(normalVector * -1f, RAYCAST_INLET, ref moveY) : 0;

        // If there is ground below us.
        if (groundChecks == 0) WhileInAir();
    }

    /*
     * RaycastX: Calculate the movement vector we need to travel along the relative X axis.
     */
    private int RaycastX(Vector2 normDir, float rawVelocity, ref Vector2 finalVel)
    {
        // If we aren't moving, save computation cycles by not doing anything.
        if ((normDir * rawVelocity) == Vector2.zero) return 0;
        // If the distance to cast is negative, turn it positive. Also compensate for RAYCAST_INLET.
        float castDist = Mathf.Abs(rawVelocity) + RAYCAST_INLET;
        if (castDist <= RAYCAST_INLET) castDist = 2 * RAYCAST_INLET;

        float closestDelta = castDist;
        int hits = 0;
        for (int i = 0; i < rayPrecisionHeight; i++)
        {
            Vector2 origin = (Vector2)formBounds.center + new Vector2(relativeHeightPoints[i].x * normDir.x, relativeHeightPoints[i].y);
            RaycastHit2D hit = Physics2D.Raycast(origin, normDir, castDist, LayerInfo.TERRAIN);

            Debug.DrawRay(origin, normDir * castDist, Color.red);
            // If the raycast hit a collider: Find the point of contact.
            if (hit)
            {
                hits++;
                if (hit.distance == 0) continue;
                float hitDist = hit.distance - RAYCAST_INLET;
                if (hitDist < closestDelta) closestDelta = hitDist;
            }
        }
        // Return the Vector2 for movement along the X axis of the entity.
        finalVel = closestDelta * normDir;
        return hits;
    }

    private int RaycastY(Vector2 normDir, float rawVelocity, ref Vector2 finalVel)
    {
        // If we aren't moving, save computation cycles by not doing anything.
        if ((normDir * rawVelocity) == Vector2.zero) return 0;
        // If the distance to cast is negative, turn it positive. Also compensate for RAYCAST_INLET.
        float castDist = Mathf.Abs(rawVelocity) + RAYCAST_INLET;
        if (castDist <= RAYCAST_INLET) castDist = 2 * RAYCAST_INLET;

        float closestDelta = castDist;
        int hits = 0;
        for (int i = 0; i < rayPrecisionBase; i++)
        {
            Vector2 origin = (Vector2)formBounds.center + new Vector2(relativeBasePoints[i].x, relativeBasePoints[i].y * normDir.y);
            RaycastHit2D hit = Physics2D.Raycast(origin, normDir, castDist, LayerInfo.TERRAIN);

            Debug.DrawRay(origin, normDir * castDist, Color.red);
            // If the raycast hit a collider: Find the point of contact.
            if (hit)
            {
                // If we are descending and we hit a collider, that means we landed on something.
                hits++;
                if (hit.distance == 0) continue;
                float hitDist = hit.distance - RAYCAST_INLET;
                if (hitDist < closestDelta) closestDelta = hitDist;
            }
        }
        // Return the Vector2 for movement along the Y axis of the entity.
        finalVel = closestDelta * normDir;
        return hits;
    }

    /*
     * Upon changing forms, a lot of hitbox information needs to change.
     * Activate the new form GameObject and deactivate the old one.
     * Cache the new form's component information into variables.
     * Calculate the relative offset points from the center of the hitbox
     * to the left and right according to the raycast precision on the respective axes.
     */
    public void ChangeForm(int index)
    {
        if (index >= forms.Count || index < 0)
        {
            Debug.LogError(gameObject + ": Tried to access form " + index + ". Only " + forms.Count + " available.");
            return;
        }
        //if (i == formIndex) return;
        forms[formIndex].SetActive(false);
        forms[index].SetActive(true);
        formObject = forms[index];
        formCollider = formObject.GetComponent<BoxCollider2D>();
        formBounds = formCollider.bounds;
        formIndex = index;

        // Reallocate the bound points in memory.
        // We must have at least 2 rays for the edges of the BoxCollider's face.
        const int MIN_RAYS = 2;
        rayPrecisionBase = Mathf.Clamp(rayPrecisionBase, MIN_RAYS, int.MaxValue);
        rayPrecisionHeight = Mathf.Clamp(rayPrecisionHeight, MIN_RAYS, int.MaxValue);
        relativeBasePoints = new Vector2[rayPrecisionBase];
        relativeHeightPoints = new Vector2[rayPrecisionHeight];

        // Calculate left to right and bottom to top the colinear points for X and Y respectively.
        formBounds.Expand(RAYCAST_INLET * -2f);
        Vector2 boxSize = formBounds.size;
        float adjustedLengthBase = boxSize.x;
        float adjustedLengthHeight = boxSize.y;
        float subLengthBase = adjustedLengthBase / (rayPrecisionBase - 1);
        float subLengthHeight = adjustedLengthHeight / (rayPrecisionHeight - 1);

        for (int i = 0; i < rayPrecisionBase; i++)
        {
            relativeBasePoints[i] = new Vector2((subLengthBase * i) - formBounds.extents.x, formBounds.extents.y);
        }

        for (int i = 0; i < rayPrecisionHeight; i++)
        {
            relativeHeightPoints[i] = new Vector2(formBounds.extents.x, (subLengthHeight * i) - formBounds.extents.y);
        }
    }

    private Vector2 CompoundVelocities(VelocityCompoundMethod over, VelocityCompoundMethod add, VelocityCompoundMethod mult)
    {
        Vector2 final = (over == null) ? Vector2.zero : over();
        if (final != Vector2.zero) return final;

        if (add != null) final += add();
        if (gravityOn) final += CalculateGravityVector(this);
        final += externalVelocity;
        if (mult != null) final.Scale(mult());

        return final;
    }

    /*
     * Can also be used externally to find the gravity vector of that entity.
     */
    public Vector2 CalculateGravityVector(EntityController en)
    {
        Vector2 dir = en.normalVector.normalized * -1f;
        float gravSpeed = (en.subAirTime * en.gravityCoeff);
        bool uncapped = en.maxGravitySpeed < 0 ? true : false;
        return dir * (uncapped ? gravSpeed : (gravSpeed > en.maxGravitySpeed ? en.maxGravitySpeed : gravSpeed));
    }

    /*================================================================================
     STATE BASED ACTION GENERALIZATION FUNCTIONS
     ================================================================================*/

    private void OnLanding()
    {
        state = EntityMotionState.GROUNDED;
        sbaOnLanding?.Invoke();
        ResetAirTime();
    }

    private void WhileInAir()
    {
        state = EntityMotionState.AIR;
        subAirTime += Time.deltaTime;
        sbaWhileInAir?.Invoke();
    }

    /*================================================================================
     UTILITY FUNCTIONS
     ================================================================================*/

    public void TallyAirTime()
    {
        totalAirTime += subAirTime;
        subAirTime = 0;
    }

    public void ResetAirTime()
    {
        totalAirTime = 0f;
        subAirTime = 0f;
    }

    /*
     * Returns the forward vector of the character based on the facingRight boolean.
     * Extrapolated since it is used in multiple places.
     */
    public Vector2 GetForwardVector()
    {
        return Vector2.right * (facingRight ? 1f : -1f);
    }

    public void ApplyVelocity(Vector2 force)
    {
        externalVelocity = force;
    }

    public void ApplyVelocity(Vector2 dir, float mag)
    {
        externalVelocity = dir * mag;
    }

    /*
     * Smoothly 
     */
    private void DecayExternalVelocity()
    {
        externalVelocity = Vector2.Lerp(externalVelocity, Vector2.zero, inertialDampening);
        //externalVelocity = Vector2.SmoothDamp(externalVelocity, Vector2.zero, ref externalVelocity, inertialDampening);
        if (Mathf.Abs(externalVelocity.y) < dampeningCutoff) externalVelocity.y = 0f;
        if (Mathf.Abs(externalVelocity.x) < dampeningCutoff) externalVelocity.x = 0f;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // First check if the Trigger entered is a hitbox.
        HitboxFramework en;
        en = collision.gameObject.GetComponent<HitboxFramework>();
        if (en == null) return;
        if (en.blacklist.Contains(faction)) return;
        en.OnHit(this);
    }

    /*
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        for (int i = 0; i < rayPrecisionHeight; i++)
        {
            Gizmos.DrawSphere((Vector2)formBounds.center + relativeHeightPoints[i], 0.05f);
        }
        for (int i = 0; i < rayPrecisionBase; i++)
        {
            Gizmos.DrawSphere((Vector2)formBounds.center + relativeBasePoints[i], 0.05f);
        }
        for (int i = 0; i < rayPrecisionHeight; i++)
        {
            Gizmos.DrawSphere((Vector2)formBounds.center + (relativeHeightPoints[i] * -1f), 0.05f);
        }
        for (int i = 0; i < rayPrecisionBase; i++)
        {
            Gizmos.DrawSphere((Vector2)formBounds.center + (relativeBasePoints[i] * -1f), 0.05f);
        }
    }
    */
}
