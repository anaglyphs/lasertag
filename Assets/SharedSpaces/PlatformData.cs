using Oculus.Platform;
using Oculus.Platform.Models;
using UnityEngine;

namespace SharedSpacesXR
{
    public class PlatformData : MonoBehaviour
    {
        public static bool Initialized { get; private set; } = false;

        public static User OculusUser { get; private set; }

        public void Start()
        {

            Debug.Log("Getting Platform Data...");

            Core.AsyncInitialize().OnComplete(message =>
            {
                if (message.IsError)
                {
                    Debug.LogError($"Error initializing Oculus platform\n{message}");
                    return;
                }

                Users.GetLoggedInUser().OnComplete(message =>
                {
                    if (message.IsError)
                    {
                        Debug.LogError($"Error getting Oculus User\n{message}");
                        Debug.LogError("Platform Data retrieval failed, restart the game now.");

                        Initialized = false;
                        return;
                    }

                    // client send host user id 
                    OculusUser = message.GetUser();

                    Debug.Log("Got Oculus User with ID: " + OculusUser.ID);
                    Debug.Log("Platform Data retrieval done, have fun!");

                    Initialized = true;
                });
            });
        }
    }
}