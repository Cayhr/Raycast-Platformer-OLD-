using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * Utility class to be used for game logic.
 */
public class ActionTimer : MonoBehaviour
{
    private bool ready = true;
    private float currentTime;
    private float maxTime;
    private float currentCooldown;
    private float maxCooldown;
    private Coroutine actionCR;
    private Coroutine cdCR;

    public bool IsReady() => ready;

    public void StartAction()
    {
        if (!ready) return;
        ready = false;
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
        if (cdCR != null)
        {
            StopCoroutine(cdCR);
        }
        cdCR = StartCoroutine(Cooldown());
    }

    private IEnumerator Cooldown()
    {
        currentTime = maxTime;
        while (currentTime > 0f)
        {
            currentTime -= Time.deltaTime;
            yield return 0;
        }
        ready = true;
    }
}
