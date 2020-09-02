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

    private PlayerControl playerControls;
    private ActionTimer dashAction, meleeAction, gunAction;

    private const float COYOTE_TIME = 5f / 60f;
    private const float CROUCH_SPEED_MULT = 0.5f;

    [Header("References")]
    [SerializeField] private GameObject swingHitbox;
    [SerializeField] private EntityController _EC;

    [Header("Runtime Statistics")]
    [SerializeField] private int jumps;
    [SerializeField] private bool crouching = false;
    [SerializeField] private bool isJumping = false;
    //[SerializeField] private bool isDashing = false;
    [SerializeField] private bool canDash = true;
    [SerializeField] private bool allowPlayerInfluence = true;

    [Header("Parameters")]
    [SerializeField] private float runSpeed;
    [SerializeField] private float jumpVelocity;
    [SerializeField] private float dashSpeed;
    [SerializeField] private float dashTime;                                // Time spent while dashing.
    [SerializeField] private float dashCooldown;                            // Dash cooldown in seconds after a dash.
    public int maxJumps;
    [SerializeField] private float meleeAttackTime;
    [SerializeField] private float meleeAttackCooldown;
    [SerializeField] private float gunAttackTime;
    [SerializeField] private float gunAttackCooldown;
    [SerializeField] private float jumpTime;
    [SerializeField] private bool canAirDash = false;

    [Header("Runtime Trackers")]
    [SerializeField] private float jumpTimeCounter;
    [SerializeField] private Vector2 dashDir = Vector2.zero;
    public Vector2 lastSwingDirection;

    private void Awake()
    {
        playerControls = new PlayerControl();

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

        playerControls.Player.AttackMelee.started += ctx =>
        {
            if (ctx.started)
                InitiateMAttack();
        };

        playerControls.Player.AttackGun.started += ctx =>
        {
            if (ctx.started)
                InitiateRAttack();
        };
    }

    // Start is called before the first frame update
    void Start()
    {
        _EC.SetVelocityFunctions(OverrideVelocities, CompoundVelocities, MultiplyVelocities);
        _EC.SetEventFunctions(OnLanding, WhileInAir);
        jumps = maxJumps;
        dashAction = gameObject.AddComponent<ActionTimer>();
        dashAction.Init(null, EndDash, dashTime, dashCooldown, 0f);
        meleeAction = gameObject.AddComponent<ActionTimer>();
        meleeAction.Init(null, EndMAttack, meleeAttackTime, meleeAttackCooldown, 0f);
        gunAction = gameObject.AddComponent<ActionTimer>();
        gunAction.Init(null, EndRAttack, gunAttackTime, gunAttackCooldown, 0f);
        swingHitbox.SetActive(false);
        _EC.faction = FactionList.PLAYER;
    }

    // Update is called once per frame
    void Update()
    {
        _EC.directionalInfluence = playerControls.Player.Move.ReadValue<Vector2>();

        if (_EC.state == EntityMotionState.AIR)
        {
            if (_EC.subAirTime == COYOTE_TIME && !isJumping)
            {
                jumps--;
            }
        }

        // While holding jump, keep going until the max jump is achieved, and then drop afterwards.
        if (isJumping)
        {
            if (jumpTimeCounter > 0)
            {
                //rb.velocity = Vector2.up * jumpVelocity;
                jumpTimeCounter -= Time.deltaTime;
            }
            else
            {
                isJumping = false;
                _EC.TallyAirTime();
            }
        }

        // Holding down to CROUCH while on the ground and you are not dashing into the ground.
        if (_EC.state == EntityMotionState.GROUNDED && !dashAction.IsActive()) crouching = _EC.directionalInfluence.y < 0 ? true : false;

        // Enable and disable the top half collider for the player when crouching.
        // TODO: Can optimize to not constantly change form!
        if (crouching) _EC.ChangeForm(1); else _EC.ChangeForm(0);
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

    private void OnLanding()
    {
        RestoreMovementOptions();
    }

    /*
     * [Ion Propulsion Jump]
     * Upon jump, set some parameters and tally the air time every jump.
     * This works on the initial off-the-ground jump, and mid-air jumps.
     */
    private void InitiateJump()
    {
        if (_EC.state == EntityMotionState.GROUNDED || _EC.totalAirTime < COYOTE_TIME || jumps > 0)
        {
            isJumping = true;
            jumpTimeCounter = jumpTime;
            _EC.state = EntityMotionState.AIR;
            crouching = false;
            _EC.TallyAirTime();
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
            _EC.ApplyVelocity(_EC.normalVector, 2f);
            _EC.TallyAirTime();
        }
    }

    /*================================================================================
     DASH ACTION LOGIC
     ================================================================================*/

    /*
     * [Gravity Suspension Dash]
     * Cancels all jumps and momentum, and then propels the player in the appointed direction.
     * With conventional WASD-like input, the dash is 8-directional, but does support any direction.
     */
    private void InitiateDash()
    {
        if (!dashAction.IsReady()) return;
        if (dashAction.IsActive()) return;      // Cannot spam dashes.
        if (!canAirDash && _EC.state == EntityMotionState.AIR) return;       // If not allowed to air dash while in the air.

        // Set some parameters immediately.
        isJumping = false;
        allowPlayerInfluence = false;

        // Save the direction the player is holding input on when the dash initiates.
        dashDir = _EC.directionalInfluence;

        // If we do not have any direction inputted for our dash, default to dashing forward.
        if (dashDir == Vector2.zero)
        {
            dashDir = _EC.GetForwardVector();
        }

        // If we are on the ground and we press dash, perform a ground dash instead.
        if (_EC.directionalInfluence.y < 0 && _EC.state == EntityMotionState.GROUNDED)
        {
            dashDir = _EC.GetForwardVector();
        }

        dashAction.StartAction();
    }

    public void EndDash()
    {
        // At the end of the dash, convert any external velocity into momentum in the direction we are going in.
        _EC.externalVelocity = dashSpeed * dashDir;
        allowPlayerInfluence = true;
        _EC.TallyAirTime();
    }

    public void InterruptDash()
    {
        dashAction.CancelNoEndAction();
        EndDash();
    }

    /*================================================================================
     MELEE ATTACK LOGIC
     ================================================================================*/

    private void InitiateMAttack()
    {
        if (!meleeAction.IsReady()) return;
        if (meleeAction.IsActive()) return;

        // Figure out the direction the swing should face.
        Vector2 dir = (_EC.directionalInfluence != Vector2.zero) ? _EC.directionalInfluence : _EC.GetForwardVector();
        lastSwingDirection = dir;

        // On ground, we cannot attack downwards.
        if (_EC.state == EntityMotionState.GROUNDED && dir.y < 0f) dir = _EC.GetForwardVector();

        PositionAndStartMAttack(dir);
    }

    private void PositionAndStartMAttack(Vector2 dir)
    {
        swingHitbox.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        swingHitbox.SetActive(true);
        meleeAction.StartAction();
    }

    private void EndMAttack()
    {
        swingHitbox.SetActive(false);
    }

    /*================================================================================
     RANGED ATTACK LOGIC
     ================================================================================*/

    private void InitiateRAttack()
    {
        if (!gunAction.IsReady()) return;
        if (gunAction.IsActive()) return;
        gunAction.StartAction();
    }

    private void EndRAttack()
    {

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
    private Vector2 OverrideVelocities()
    {
        Vector2 final = Vector2.zero;
        if (dashAction.IsActive())
        {
            if (crouching) final.x = _EC.GetForwardVector().x * dashSpeed * 1.1f;
            else final.x = dashDir.x * dashSpeed;
            final.y = dashDir.y * dashSpeed;
        }
        return final;
    }

    private Vector2 CompoundVelocities()
    {
        Vector2 final = Vector2.zero;
        final.x += (_EC.directionalInfluence.x * runSpeed) * (allowPlayerInfluence ? 1f : 0f);
        if (isJumping) final += (_EC.normalVector * jumpVelocity);
        return final;
    }

    private Vector2 MultiplyVelocities()
    {
        Vector2 final = Vector2.one;
        if (_EC.state == EntityMotionState.GROUNDED) final.x = crouching ? CROUCH_SPEED_MULT : 1f;
        return final;
    }

    private void HitCeiling()
    {
        //rb.velocity = new Vector2(rb.velocity.x, jumpVelocity/4f);
        _EC.TallyAirTime();
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
    private void OnEnable()
    {
        playerControls.Enable();
    }

    private void OnDisable()
    {
        playerControls.Disable();
    }
}
