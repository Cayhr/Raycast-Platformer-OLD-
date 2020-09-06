using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;
using UnityEngine.Events;
using UnityEngine.UI;

/* PlayerController | Kevin Chau
 * 
 * 
 */

public class PlayerController : MonoBehaviour
{
    public UnityEvent e_Land;

    private const string PLAYER_PROJECTILE_POOL_NAME = "Player";
    private ObjectPoolController OBJ_POOLER;
    private PlayerControl playerControls;
    private ActionTimer dashAction, attackAction;

    private const float COYOTE_TIME = 5f / 60f;
    private const float CROUCH_SPEED_MULT = 0.5f;

    [Header("References")]
    [SerializeField] private EntityController _EC;
    [SerializeField] private GameObject swingHitbox;
    [SerializeField] private GameObject[] bulletPrefabs;
    [SerializeField] private SpriteRenderer swingHitboxSprite;
    private const float COLOR_MAX = 255f;
    private Color heatColor = new Color(255/COLOR_MAX, 100/COLOR_MAX, 100/COLOR_MAX);
    private Color currentColor = Color.white;
    [SerializeField] private Slider heatSlider, hpSlider;

    [Header("Runtime Statistics")]
    private int jumps;
    private bool crouching = false;
    private bool isJumping = false;
    private bool allowPlayerInfluence = true;

    [Header("Melee Attack Parameters")]
    [SerializeField] private float meleeAttackTime;
    [SerializeField] private float meleeAttackCooldown;
    public float heatConsumption;

    [Header("Gun Attack Parameters")]
    [SerializeField] private float gunDamage;
    public float currentHeat, maxHeat, generatedHeat, heatDecayPerSec, heatDecayDelay, heatDelayCounter;
    public bool criticalHeat;
    [SerializeField] private float gunAttackTime;
    [SerializeField] private float gunAttackCooldown;

    [Header("Movement Parameters")]
    [SerializeField] private float runSpeed;
    [SerializeField] private float jumpVelocity;
    [SerializeField] private float dashSpeed;
    [SerializeField] private float dashTime;                                // Time spent while dashing.
    [SerializeField] private float dashCooldown;                            // Dash cooldown in seconds after a dash.
    public int maxJumps;
    [SerializeField] private float jumpTime;
    [SerializeField] private bool canAirDash = false;
    private int totalBulletPrefabs;

    [Header("Runtime Trackers")]
    private float jumpTimeCounter;
    private Vector2 dashDir = Vector2.zero;
    public Vector2 lastSwingDirection;

    private void Awake()
    {
        // Initialize input manager stuff.
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

        // Initialize some variables.
        totalBulletPrefabs = bulletPrefabs.Length;
    }

    // Start is called before the first frame update
    void Start()
    {
        OBJ_POOLER = ObjectPoolController.SharedInstance;
        OBJ_POOLER.CreateObjPool(PLAYER_PROJECTILE_POOL_NAME, bulletPrefabs[0], true);
        _EC.SetVelocityFunctions(OverrideVelocities, CompoundVelocities, MultiplyVelocities);
        _EC.SetEventFunctions(OnLanding, WhileInAir);
        jumps = maxJumps;
        dashAction = gameObject.AddComponent<ActionTimer>();
        dashAction.Init(null, EndDash, dashTime, dashCooldown, 0f);
        attackAction = gameObject.AddComponent<ActionTimer>();
        attackAction.Init(null, null, 0f, 0f, 0f);
        swingHitbox.SetActive(false);
        _EC.faction = FactionList.PLAYER;
        heatSlider.maxValue = maxHeat;
        heatSlider.minValue = 0f;
        heatSlider.value = currentHeat;
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

        // Decay heat.
        if (currentHeat > 0f && heatDelayCounter <= 0f)
        {
            currentHeat -= heatDecayPerSec * Time.deltaTime;
            heatSlider.value = currentHeat;
            if (currentHeat <= 0f)
            {
                criticalHeat = false;
                currentHeat = 0f;
            }
        }
        if (heatDelayCounter > 0f)
        {
            heatDelayCounter -= Time.deltaTime;
        }
        if (currentColor != Color.white)
        {
            swingHitboxSprite.color = currentColor;
            currentColor = Color.Lerp(currentColor, Color.white, 0.1f);
        }
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

#region Jump Logic

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
            _EC.ApplyVelocity(Vector2.up, 2f);
            _EC.TallyAirTime();
        }
    }

#endregion

#region Dash Action Logic

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

#endregion

#region Melee Attack Logic

    private void InitiateMAttack()
    {
        if (!CanAttack()) return;

        // Calculate if we consume heat.
        if (currentHeat > 0f && criticalHeat)
        {
            currentColor = heatColor;
        }

        // Calculate the combo we are currently on.
        attackAction.SetFunctions(null, EndOfMAttack);
        attackAction.SetTimes(meleeAttackTime, meleeAttackCooldown);

        Vector2 dir = GetDirectionOfAttack();

        // We cache the lastSwingDirection to store the direction player swung in at the moment of attack.
        lastSwingDirection = dir;
        swingHitbox.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        swingHitbox.SetActive(true);
        attackAction.StartAction();
    }

    private void EndOfMAttack()
    {
        swingHitbox.SetActive(false);
    }

#endregion

#region Ranged Attack Logic

    private void InitiateRAttack()
    {
        // We cannot fire if we are doing another attack or overheated.
        if (!CanAttack() || criticalHeat || currentHeat >= maxHeat) return;
        currentHeat += generatedHeat;
        if (currentHeat >= maxHeat)
        {
            criticalHeat = true;
            currentHeat = maxHeat;
        }

        SetHeatSlider(currentHeat);

        // We always have a delay before heat decays.
        heatDelayCounter = heatDecayDelay;

        attackAction.SetFunctions(null, EndOfRAttack);
        attackAction.SetTimes(gunAttackTime, gunAttackCooldown);

        GameObject bullet = OBJ_POOLER.PullFromPool(PLAYER_PROJECTILE_POOL_NAME);
        Vector2 dir = GetDirectionOfAttack();
        bullet.transform.position = (Vector2)transform.position + dir;
        bullet.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x));
        HB_PlayerEnergyShot script = bullet.GetComponent<HB_PlayerEnergyShot>();
        script.speed = 45f;
        script.owner = gameObject;
        Rigidbody2D projRigid = bullet.GetComponent<Rigidbody2D>();
        projRigid.velocity = (dir * script.speed);
        attackAction.StartAction();
    }

    private void EndOfRAttack()
    {

    }

    #endregion

#region Velocity Override Functions

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
        if (isJumping) final.y += jumpVelocity;
        return final;
    }

    private Vector2 MultiplyVelocities()
    {
        Vector2 final = Vector2.one;
        if (_EC.state == EntityMotionState.GROUNDED) final.x = crouching ? CROUCH_SPEED_MULT : 1f;
        return final;
    }

#endregion

#region Utility Functions

    public void SetHeatSlider(float current)
    {
        heatSlider.value = current;
        heatSlider.maxValue = maxHeat;
    }

    private bool CanAttack()
    {
        if (!attackAction.IsReady()) return false;
        if (attackAction.IsActive()) return false;
        return true;
    }

    private Vector2 GetDirectionOfAttack()
    {
        // Figure out the direction the swing should face.
        Vector2 dir = (_EC.directionalInfluence != Vector2.zero) ? _EC.directionalInfluence : _EC.GetForwardVector();

        // On ground, we cannot attack downwards.
        if (_EC.state == EntityMotionState.GROUNDED && dir.y < 0f) dir = _EC.GetForwardVector();

        return dir;
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

#endregion

    private void OnEnable()
    {
        playerControls.Enable();
    }

    private void OnDisable()
    {
        playerControls.Disable();
    }
}
