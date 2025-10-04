using System;
using UnityEngine;

public class DieSpawnNotifier : MonoBehaviour
{
    public static event Action OnFirstDieSpawned;

    private void Start()
    {
        OnFirstDieSpawned?.Invoke();
    }
}
