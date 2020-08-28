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
    private bool ready = true;
    private bool inAction = false;
    private float currentTime, currentCooldown;
    private float maxTime, maxCooldown;
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

    public void SetTimes(float newTime, float newCooldown)
    {
        maxTime = newTime;
        maxCooldown = newCooldown;
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
            currentCooldown -= Time.deltaCooldown;
            yield return 0;
        }
        ready = true;
    }
}
