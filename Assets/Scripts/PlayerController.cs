using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;
using UnityEngine.Events;

/* PlayerController | Kevin Chau
 * 
 * 
 */

public class PlayerController : MonoBehaviour
{
    public UnityEvent e_Land;

    private enum MotionState { GROUNDED, AIR, CLUTCH };
    private Rigidbody2D rb;
    private PlayerControl playerControls;
    private CircleCollider2D mainCollider;
    private BoxCollider2D headCollider;

    private LayerMask terrainLayer;

    private const float COYOTE_TIME = 5f / 60f;
    private const float CROUCH_SPEED_MULT = 0.5f;

    [Header("Runtime Statistics")]
    [SerializeField] private MotionState state;
    [SerializeField] private Vector2 currentVelocity = Vector2.zero;
    [SerializeField] private Vector2 externalVelocity = Vector2.zero;
    [SerializeField] private int jumps;
    [SerializeField] private bool crouching = false;
    [SerializeField] private bool facingRight = true;
    [SerializeField] private bool isJumping = false;
    [SerializeField] private bool isDashing = false;
    [SerializeField] private bool canDash = true;
    [SerializeField] private bool allowPlayerInfluence = true;

    [Header("Parameters")]
    [SerializeField] private float runSpeed;
    [SerializeField] private float jumpVelocity;
    [SerializeField] private float dashSpeed;
    public int maxJumps;
    [SerializeField] private float jumpTime;
    [SerializeField] private bool canAirDash = false;
    [SerializeField] private float gravityCoeff;
    [SerializeField] private float maxDescentSpeed;

    [Header("Runtime Trackers")]
    [SerializeField] private float subAirTime = 0;
    [SerializeField] private float totalAirTime = 0;
    [SerializeField] private float dashTime;                                // Time spent while dashing.
    [SerializeField] private float currentDashTime;                         // Counter for time in current dash action.
    [SerializeField] private float dashCooldown;                            // Dash cooldown in seconds after a dash.
    [SerializeField] private float dashCooldownCounter;                     // Counter for time between dashes.
    [SerializeField] private float jumpTimeCounter;
    [SerializeField] private Vector2 directionalInfluence = Vector2.zero;
    [SerializeField] private Vector2 dashDir = Vector2.zero;

    private void Awake()
    {
        playerControls = new PlayerControl();
        terrainLayer = LayerMask.GetMask("Terrain");

        playerControls.Player.Jump.started += ctx =>
        {
            if (ctx.started)
                InitiateJump();
        };

        playerControls.Player.Jump.canceled += ctx =>
        {
            if (ctx.canceled)
                ReleasedJump();
        };

        playerControls.Player.Dash.started += ctx =>
        {
            if (ctx.started)
                InitiateDash();
        };
    }

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        mainCollider = GetComponent<CircleCollider2D>();
        headCollider = GetComponent<BoxCollider2D>();
        jumps = maxJumps;
    }

    // Update is called once per frame
    void Update()
    {
        directionalInfluence = playerControls.Player.Move.ReadValue<Vector2>();

        // Raycast up and down to check for floors.
        RaycastHit2D floorCheck = Physics2D.CircleCast((Vector2)transform.position + mainCollider.offset, 0.49f, Vector2.down, 0.05f, terrainLayer);
        //RaycastHit2D ceilingCheck = Physics2D.CircleCast((Vector2)transform.position + headCollider.offset, 0.49f, Vector2.up, 0.05f, terrainLayer);

        // If there is ground below us.
        if (floorCheck.collider != null)
        {
            // If we just hit the ground coming out from another state, reset jumps and air statistics.
            if (state != MotionState.GROUNDED) OnLanding();
        }
        // If we don't detect any ground below us, go ahead and fall off.
        else
        {
            // TODO: One-time "we left the ground" check.
            // The first frame we leave coyote time, subtract a jump.
            if (subAirTime == COYOTE_TIME && !isJumping)
            {
                jumps--;
            }
            state = MotionState.AIR;
            subAirTime += Time.deltaTime;

            WhileInAir();
        }

        // Regenerate the dash action.
        if (!isDashing && dashCooldownCounter > 0)
        {
            dashCooldownCounter -= Time.deltaTime;
        }

        // While holding jump, keep going until the max jump is achieved, and then drop afterwards.
        if (isJumping)
        {
            if (jumpTimeCounter > 0)
            {
                rb.velocity = Vector2.up * jumpVelocity;
                jumpTimeCounter -= Time.deltaTime;
            }
            else
            {
                isJumping = false;
                TallyAirTime();
            }
        }

        // Holding down to CROUCH while on the ground and you are not dashing into the ground.
        if (state == MotionState.GROUNDED && !isDashing) crouching = directionalInfluence.y < 0 ? true : false;
        if (crouching)
        {
            headCollider.enabled = false;
        }
        else
        {
            headCollider.enabled = true;
        }

        if (directionalInfluence.x > 0) facingRight = true;
        if (directionalInfluence.x < 0) facingRight = false;
    }

    /*
     * We set the velocity of the rigidbody to the final calculated velocity, but also store that information.
     */
    private void FixedUpdate()
    {
        Vector2 finalVelocity = new Vector2(CompoundXVelocities(), CompoundYVelocities());
        currentVelocity = finalVelocity;
        rb.velocity = finalVelocity;
        DecayExternalVelocity(0.1f);
    }

    /*
     * Logic for any effects that must occur while in the air.
     */
    private void WhileInAir()
    {
        // You can't be crouching in mid-air. Down inputs while in the air should be read from DI.
        // Ensuring the player state is never crouching while in the air also prevents retaining crouch state if you walk off an edge.
        // NOTE: Potential optimization is possible for a 1-time "step off ground" detection to set crouching to false.
        crouching = false;
    }

    /*
     * When the player lands on the ground, reset stats and set the state appropriately.
     */
    private void OnLanding()
    {
        state = MotionState.GROUNDED;
        RestoreMovementOptions();
        ResetAirTime();
    }

    /*
     * [Ion Propulsion Jump]
     * Upon jump, set some parameters and tally the air time every jump.
     * This works on the initial off-the-ground jump, and mid-air jumps.
     */
    private void InitiateJump()
    {
        if (state == MotionState.GROUNDED || totalAirTime < COYOTE_TIME || jumps > 0)
        {
            isJumping = true;
            jumpTimeCounter = jumpTime;
            crouching = false;
            TallyAirTime();
            jumps--;
        }
    }

    /*
     * Logic for when the jump button is released.
     * By setting isJumping to false and calling TallyAirTime(), the player gets complete height control when releasing mid jump.
     */
    private void ReleasedJump()
    {
        if (isJumping)
        {
            isJumping = false;
            ApplyVelocity(Vector2.up, 2f);
            TallyAirTime();
        }
    }

    /*
     * [Gravity Suspension Dash]
     * Cancels all jumps and momentum, and then propels the player in the appointed direction.
     * With conventional WASD-like input, the dash is 8-directional, but does support any direction.
     */
    private void InitiateDash()
    {
        if (dashCooldownCounter > 0) return;
        if (isDashing) return;                                  // Cannot spam dashes.
        if (!canAirDash && state == MotionState.AIR) return;    // If not allowed to air dash while in the air.

        // Set some parameters immediately.
        isJumping = false;
        isDashing = true;
        allowPlayerInfluence = false;

        // Save the direction the player is holding input on when the dash initiates.
        dashDir = new Vector2(directionalInfluence.x, directionalInfluence.y);

        // If we do not have any direction inputted for our dash, default to dashing forward.
        if (dashDir == Vector2.zero)
        {
            dashDir = GetForwardVector();
        }

        // If we are on the ground and we press dash, perform a ground dash instead.
        if (directionalInfluence.y < 0 && state == MotionState.GROUNDED)
        {
            dashDir = GetForwardVector();
            Debug.Log("Crouch slide!");
            CrouchSlide();
            return;
        }
        StartCoroutine(DashCoroutine(false));
    }

    /*
     * [Grounded Slide Dash]
     * Alternate dash sequence that only functions when the player is on the ground.
     * It only occurs on the ground while crouching, and grants a damaging hitbox.
     */
    private void CrouchSlide()
    {
        // Extra logic for altered hitboxes, animations, and stats.
        StartCoroutine(DashCoroutine(true));
    }

    /*
     * Moves the player in the direction provided, with magnitude = dashSpeed.
     * NOTE: |dir| = 1, if x and y are non-zero, they will be ~0.71 (sine/cosine at 45 degree notches).
     */
    private IEnumerator DashCoroutine(bool crouched)
    {
        currentDashTime = dashTime;
        while(currentDashTime > 0) {
            crouching = crouched;
            //if (rb.IsTouchingLayers(terrainLayer) && state == MotionState.GROUNDED)
            //{
            //    EndDash();
            //}
            currentDashTime -= Time.deltaTime;
            yield return 0;
        }
        EndDash();
    }

    /*
     * 
     */
    public void EndDash()
    {
        isDashing = false;
        allowPlayerInfluence = true;
        currentDashTime = 0f;
        dashCooldownCounter = dashCooldown;
        TallyAirTime();
    }

    /*
     * The next two functions are axis velocity compound methods.
     * They total the amount of velocity the player will have on the X and Y axis.
     * There are three sections: Overrides, Compounds, and Multipliers.
     * The Overrides section goes first, as an optimization for logic.
     *  As the name implies, it is any total override of velocity on that axis.
     * The Compounds section goes second, and adds up sources of velocity.
     *  It must go after Overrides and before Multipliers, for mathematical purposes.
     * The Multipliers section goes third, and is a final multiplier on any velocity.
     */
    private float CompoundXVelocities()
    {
        // Baseline set total to player influence run speed.
        float total = 0f;

        // Overrides
        if (isDashing && crouching)
        {
            return GetForwardVector().x * dashSpeed * 1.1f;
        }
        else if (isDashing)
        {
            return dashDir.x * dashSpeed;
        }

        // Compounds
        total += (directionalInfluence.x * runSpeed) * (allowPlayerInfluence ? 1f : 0f);
        total += externalVelocity.x;

        // Multipliers
        if (state == MotionState.GROUNDED)
        {
            total *= crouching ? CROUCH_SPEED_MULT : 1f;
        }

        return total;
    }

    private float CompoundYVelocities()
    {
        float total = 0f;

        // Overrides
        if (isDashing)
        {
            return dashDir.y * dashSpeed;
        }

        // Compounds 
        float totalGrav = subAirTime * gravityCoeff;
        total -= totalGrav > maxDescentSpeed ? maxDescentSpeed : totalGrav;
        total += externalVelocity.y;

        // Multipliers
        if (isJumping)
            total += jumpVelocity;

        return total;
    }

    private void HitCeiling()
    {
        //rb.velocity = new Vector2(rb.velocity.x, jumpVelocity/4f);
        TallyAirTime();
    }

    public void RestoreMovementOptions()
    {
        jumps = maxJumps;
        jumpTimeCounter = jumpTime;
    }

    public void SetJumps(int j)
    {
        jumps = j;
    }

    public void TallyAirTime()
    {
        totalAirTime += subAirTime;
        subAirTime = 0;
    }

    private void ResetAirTime()
    {
        totalAirTime = 0f;
        subAirTime = 0f;
    }

    /*
     * Returns the forward vector of the character based on the facingRight boolean.
     * Extrapolated since it is used in multiple places.
     */
    private Vector2 GetForwardVector()
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

    private void OnEnable()
    {
        playerControls.Enable();
    }

    private void OnDisable()
    {
        playerControls.Disable();
    }
}
