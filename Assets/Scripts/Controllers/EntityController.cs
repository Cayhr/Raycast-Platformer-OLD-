using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public delegate Vector2 VelocityCompoundMethod();
public delegate void StateChangeAction();
public enum FactionList { NEUTRAL, PLAYER, ENEMIES};
public enum EntityMotionState { GROUNDED, AIR, CLUTCH };

public class EntityController : MonoBehaviour
{
    private Rigidbody2D rb;
    private static readonly float RAYCAST_INLET = 0.015f;
    //public CircleCollider2D mainCollider;
    //public BoxCollider2D headCollider;
    private VelocityCompoundMethod vOverride, vAdd, vMult;
    private StateChangeAction eOnLanding;

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
    public float gravityCoeff, maxGravitySpeed, inertialDampening;
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

    public void SetEventFunctions(StateChangeAction landing)
    {
        eOnLanding = landing;
    }

    public void Update()
    {
        // Raycast up and down to check for floors.
        //RaycastHit2D floorCheck = Physics2D.CircleCast((Vector2)transform.position + mainCollider.offset, mainCollider.radius, normalVector * -1f, 0.05f, LayerInfo.TERRAIN);
        //RaycastHit2D ceilingCheck = Physics2D.CircleCast((Vector2)transform.position + headCollider.offset, 0.49f, Vector2.up, 0.05f, terrainLayer);

        // If there is ground below us.
        //if (floorCheck.collider != null)
        //if (true)
        //{
        //    // If we just hit the ground coming out from another state, reset jumps and air statistics.
        //    if (state != EntityMotionState.GROUNDED) OnLanding();
        //}
        // If we don't detect any ground below us, go ahead and fall off.
        //else
        //{
            // TODO: One-time "we left the ground" check.
            // The first frame we leave coyote time, subtract a jump.
            //state = EntityMotionState.AIR;
            //subAirTime += Time.deltaTime;

            //WhileInAir();
        //}
        if (directionalInfluence.x > 0) facingRight = true;
        else if (directionalInfluence.x < 0) facingRight = false;
        directionMultiplier = facingRight ? 1f : -1f;

        // First calculate the final velocity of the entity.
        currentVelocity = CompoundVelocities(vOverride, vAdd, vMult);

        Vector2 castLen = currentVelocity * Time.deltaTime;

        Vector2 moveX = RaycastX(Vector2.right * Mathf.Sign(castLen.x), Mathf.Abs(castLen.x));
        Vector2 moveY = RaycastY(Vector2.up * Mathf.Sign(castLen.y), Mathf.Abs(castLen.y));

        //rb.MovePosition((Vector2)transform.position + toMove);
        transform.Translate(moveX + moveY);
        Physics2D.SyncTransforms();

        // Check for ground.
        if (state == EntityMotionState.GROUNDED)
        {

        }
        DecayExternalVelocity(inertialDampening);
    }

    public void FixedUpdate()
    {
    }

    /*
     * Cast Distance will always be a positive number.
     */
    private Vector2 RaycastX(Vector2 normDir, float len)
    {
        // If we aren't moving, save computation cycles by not doing anything.
        if ((normDir * len) == Vector2.zero) return Vector2.zero;
        // If the distance to cast is negative, turn it positive. Also compensate for RAYCAST_INLET.
        float castDist = ((len < 0f) ? Mathf.Abs(len) : len) + RAYCAST_INLET;
        if (castDist <= RAYCAST_INLET) castDist = 2 *RAYCAST_INLET;

        float closestDelta = len;
        for (int i = 0; i < rayPrecisionHeight; i++)
        {
            Vector2 origin = (Vector2)formBounds.center + new Vector2(relativeHeightPoints[i].x * directionMultiplier, relativeHeightPoints[i].y);
            RaycastHit2D hit = Physics2D.Raycast(origin, normDir, castDist, LayerInfo.TERRAIN);

            Debug.DrawRay(origin, Vector2.right * directionMultiplier * castDist, Color.red);
            // If the raycast hit a collider: Find the point of contact.
            if (hit)
            {
                if (hit.distance == 0) continue;
                //Debug.Log(i + ") From: " + origin + ", to: " + hit.point);
                float hitDist = hit.distance - RAYCAST_INLET;
                if (hitDist < closestDelta) closestDelta = hitDist;
                //castDist = hit.distance;
            }
            // If the raycast did not hit a collider: Find the point in empty space.
            //else Debug.DrawRay(origin, (normDir * castDist), Color.green);
             //+ (normDir * RAYCAST_INLET * directionMultiplier)
            //float delta = (endPoint - origin).magnitude - RAYCAST_INLET;
            // After getting the endpoint either of contact or in empty space, 
        }

        // Whichever point was closest, translate the entity to that point.
        //Debug.Log("X Move from: " + ((Vector2)transform.position).ToString() + ", to: " + ((Vector2)transform.position + (normDir * closestDelta)));
        //transform.Translate(castDist * directionMultiplier, 0, 0);
        return closestDelta * normDir;
    }

    private Vector2 RaycastY(Vector2 normDir, float len)
    {
        if (normDir == Vector2.zero) return Vector2.zero;
        return Vector2.zero;
    }

    // CODE GRAVEYARD
        //rb.velocity = currentVelocity;
        //rb.MovePosition((Vector2)transform.position + (currentVelocity) * Time.deltaTime);
        // TODO: Cast the current form's collider into a direction. On hit, set the position of the Entity to the centroid of the collision moment.
        //rb.MovePosition((Vector2)transform.position + new Vector2(Time.fixedDeltaTime, 0f));

            //if (cast.centroid != null)
            //{
            //    Debug.Log(gameObject + " is casted to hit " + cast.collider.gameObject + ", at point " + cast.centroid);
            //    rb.MovePosition(cast.centroid);
            //}
            //if (cast.collider == null)
            //{
            //    state = EntityMotionState.AIR;
            //    subAirTime += Time.fixedDeltaTime;
            //}

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

    private void OnLanding()
    {
        state = EntityMotionState.GROUNDED;
        eOnLanding?.Invoke();
        ResetAirTime();
    }

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
    private void DecayExternalVelocity(float rate)
    {
        externalVelocity = Vector2.Lerp(externalVelocity, Vector2.zero, rate);
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
