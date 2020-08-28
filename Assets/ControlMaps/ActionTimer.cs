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
    [SerializeField] private float currentTime, currentCooldown;
    [SerializeField] private float maxTime, maxCooldown;
    private Coroutine actionCR;
    private Coroutine cdCR;

    private StartOfAction startFn;
    private EndOfAction endFn;

    public ActionTimer(StartOfAction _startFn, EndOfAction _endFn, float _maxTime, float _maxCooldown)
    {
        maxTime = _maxTime;
        maxCooldown = _maxCooldown;
        currentTime = currentCooldown = 0f;
        startFn = _startFn;
        endFn = _endFn;
    }

    public bool IsReady() => ready;
    public bool IsActive() => inAction;
    public float ActiveTime() => currentTime;
    public float CooldownTime () => currentCooldown;

    private void Start()
    {
        ready = true;
        inAction = false;
    }

    public void SetTimes(float newTime, float newCooldown)
    {
        currentTime = currentCooldown = 0f;
        maxTime = newTime;
        maxCooldown = newCooldown;
    }

    public void SetFunctions(StartOfAction _startFn, EndOfAction _endFn)
    {
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
        currentTime = maxTime;
        while (currentTime > 0f)
        {
            currentTime -= Time.deltaTime;
            yield return 0;
        }
        EndAction();
    }

    private void EndAction()
    {
        inAction = false;
        currentTime = 0f;
        currentCooldown = maxCooldown;

        // Play whatever function was desired 
        endFn?.Invoke();
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
