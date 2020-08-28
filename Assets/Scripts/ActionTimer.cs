using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public delegate void StartOfAction();
public delegate void EndOfAction();

/*
 * Utility class to be used for game logic.
 * The ActionTimer is perfect for moves that have a process once started, and a cooldown before it can happen again.
 * When an ActionTimer is initialized, specify the functions that will start at the beginning and end of the process.
 * In the Entity utilizing the ActionTimer, use IsActive() to check if the action is being performed.
 */
public class ActionTimer : MonoBehaviour
{
    [Header("Status")]
    [SerializeField] private bool ready = true;
    [SerializeField] private bool inAction = false;

    [Header("Runtime Trackers")]
    [SerializeField] private float currentTime;
    [SerializeField] private float currentCooldown;
    [SerializeField] private float delay;
    [SerializeField] private float maxTime;
    [SerializeField] private float maxCooldown;

    private Coroutine actionCR;
    private Coroutine cdCR;

    private StartOfAction startFn;
    private EndOfAction endFn;

    public bool IsReady() => ready;
    public bool IsActive() => inAction;
    public float ActiveTime() => currentTime;
    public float CooldownTime () => currentCooldown;

    // Baseline initializtion.
    private void Awake()
    {
        ready = false;
        inAction = false;
        startFn = null;
        endFn = null;
        currentTime = currentCooldown = delay = maxTime = maxCooldown = 0f;
    }

    /*
     * Disallow anything to be done until initialized.
     */
    public void Init(StartOfAction _startFn, EndOfAction _endFn, float newTime, float newCooldown, float delay)
    {
        ready = true;
        SetFunctions(_startFn, _endFn);
        SetTimes(newTime, newCooldown);
        SetDelay(delay);
    }

    public void CancelWithEndAction()
    {
        CancelNoEndAction();
        endFn?.Invoke();
    }

    // If the ActionTimer is in progress, stop it pre-maturely.
    public void CancelNoEndAction()
    {
        if (inAction && actionCR != null)
        {
            StopCoroutine(actionCR);
            ResetAndCooldown();
        }
    }

    public void ReduceCooldown(float seconds) => currentCooldown -= seconds;

    public void RefreshCooldown()
    {
        if (cdCR != null) StopCoroutine(cdCR);
        currentCooldown = 0f;
        ready = true;
        inAction = false;
    }

    public void SetDelay(float d)
    {
        if (!ready)
        {
            Debug.LogWarning("Setting delay for " + this + " when Timer is not ready, or unitialized");
            return;
        }
        delay = d;
    }

    public void SetTimes(float newTime, float newCooldown)
    {
        if (!ready)
        {
            Debug.LogWarning("Changing times for " + this + " when Timer is not ready, or unitialized");
            return;
        }
        currentTime = currentCooldown = 0f;
        maxTime = newTime;
        maxCooldown = newCooldown;
    }

    public void SetFunctions(StartOfAction _startFn, EndOfAction _endFn)
    {
        if (!ready)
        {
            Debug.LogWarning("Changing functions for " + this + " when Timer is not ready, or unitialized");
            return;
        }
        startFn = _startFn;
        endFn = _endFn;
    }

    public void StartAction()
    {
        if (!ready) return;
        ready = false;
        inAction = true;
        startFn?.Invoke();
        if (actionCR != null)
        {
            StopCoroutine(actionCR);
        }
        actionCR = StartCoroutine(InAction());
    }

    private IEnumerator InAction()
    {
        currentTime = maxTime + delay;
        while (currentTime > 0f)
        {
            currentTime -= Time.deltaTime;
            yield return 0;
        }
        EndAction();
    }

    private void EndAction()
    {
        // Play whatever function was desired 
        endFn?.Invoke();
        ResetAndCooldown();
    }

    private void ResetAndCooldown()
    {
        inAction = false;
        currentTime = 0f;

        if (cdCR != null)
        {
            StopCoroutine(cdCR);
        }
        cdCR = StartCoroutine(Cooldown());
    }

    private IEnumerator Cooldown()
    {
        currentCooldown = maxCooldown;
        while (currentCooldown > 0f)
        {
            currentCooldown -= Time.deltaTime;
            yield return 0;
        }
        ready = true;
    }
}
