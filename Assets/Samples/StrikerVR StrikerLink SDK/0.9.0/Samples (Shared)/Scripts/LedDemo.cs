using StrikerLink.Unity.Runtime.Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrikerLink.Unity.Runtime.Samples
{
    public class LedDemo : MonoBehaviour
    {
        public StrikerDevice device;

        int cycleIndex = 0;
        int effectIndex = 0;

        List<Color> solidColours = new List<Color>()
        {
            Color.yellow,
            Color.cyan,
            Color.red,
            Color.green,
            Color.blue,
            Color.magenta,
            Color.grey,
            Color.white,
        };

        private void Awake()
        {
            if (device == null)
                device = GetComponent<StrikerDevice>();
        }

        // Update is called once per frame
        void Update()
        {
            CycleEffect();
            CycleTouchpads();
        }

        void CycleTouchpads()
        {
            if (device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.TouchpadLeft))
                cycleIndex--;

            if (device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.TouchpadRight))
                cycleIndex++;

            if (cycleIndex < 0)
                cycleIndex = solidColours.Count - 1;

            if (cycleIndex >= solidColours.Count)
                cycleIndex = 0;

            if(device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.TouchpadLeft) || device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.TouchpadRight))
                PlayEffect();
        }

        void CycleEffect()
        {
            if (device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.SideLeft))
                effectIndex--;

            if (device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.SideRight))
                effectIndex++;

            if (effectIndex < 0)
                effectIndex = 3;

            if (effectIndex > 3)
                effectIndex = 0;

            if (device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.SideLeft) || device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.SideRight))
                PlayEffect();
        }

        void PlayEffect()
        {
            if (effectIndex == 0)
            {
                device.PlaySolidLedEffect(solidColours[cycleIndex], 0, Shared.Devices.Types.DeviceMavrik.LedGroup.TopLine);
                device.PlaySolidLedEffect(solidColours[cycleIndex], 0, Shared.Devices.Types.DeviceMavrik.LedGroup.FrontRings);
            }
            else if (effectIndex == 1)
            {
                device.PlayFlashLedEffect(solidColours[cycleIndex], Color.black, 0.5f, 1, Shared.Devices.Types.DeviceMavrik.LedGroup.TopLine);
                device.PlayFlashLedEffect(solidColours[cycleIndex], Color.black, 0.5f, 1, Shared.Devices.Types.DeviceMavrik.LedGroup.FrontRings);
            }
            else if (effectIndex == 2)
            {
                device.PlayPulseLedEffect(solidColours[cycleIndex], Color.black, 0.5f, 1, Shared.Devices.Types.DeviceMavrik.LedGroup.TopLine);
                device.PlayPulseLedEffect(solidColours[cycleIndex], Color.black, 0.5f, 1, Shared.Devices.Types.DeviceMavrik.LedGroup.FrontRings);
            }
            else if (effectIndex == 3)
            {
                device.PlayForwardLedEffect(solidColours[cycleIndex], Color.black, 0.5f, 1, Shared.Devices.Types.DeviceMavrik.LedGroup.TopLine);
                device.PlayForwardLedEffect(solidColours[cycleIndex], Color.black, 0.5f, 1, Shared.Devices.Types.DeviceMavrik.LedGroup.FrontRings);
            }
        }
    }
}