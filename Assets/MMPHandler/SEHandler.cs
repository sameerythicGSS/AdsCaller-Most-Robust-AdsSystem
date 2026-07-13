using System;
using UnityEngine;


    // Renamed with the "Out" prefix for core project scripts
    public class SEHandler : MonoBehaviour
    {
        private static SEHandler _instance;
        public static SEHandler Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<SEHandler>();

                    if (_instance == null)
                        Debug.LogError("SEHandler instance not found in scene! Please add it to a persistent GameObject.");
                    else
                        DontDestroyOnLoad(_instance.gameObject);
                }
                return _instance;
            }
        }

        internal void LogEvent(string v)
        {
            Logger.Note("Mock event log: " + v);
        }

        void Log(string log) => Logger.Note(log);
    }
