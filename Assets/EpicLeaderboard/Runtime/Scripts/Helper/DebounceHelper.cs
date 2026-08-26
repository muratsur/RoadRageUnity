using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EpicLeaderboard
{
    public static class MonoBehaviourExtensions
    {
        private static Dictionary<string, Coroutine> debounceCoroutines = new();
    
        public static void Debounce(this MonoBehaviour mb, string key, float delay, Action action)
        {
            if (debounceCoroutines.TryGetValue(key, out var existing) && existing != null)
                mb.StopCoroutine(existing);
        
            debounceCoroutines[key] = mb.StartCoroutine(DebounceRoutine(key, delay, action));
        }
    
        private static IEnumerator DebounceRoutine(string key, float delay, Action action)
        {
            yield return new WaitForSeconds(delay);
            debounceCoroutines.Remove(key);
            action();
        }
    }

}