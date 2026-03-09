using UnityEngine;
using MXR.SDK;

namespace App
{
    public static class DeviceIdentity
    {
        public const string Unknown = "unknown";

        public static string GetMxrSerial()
        {
            string serial = MXRManager.System?.DeviceStatus?.serial;
            if (!string.IsNullOrWhiteSpace(serial))
            {
                return serial;
            }

            return null;
        }

        public static string GetAndroidSerial()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var buildClass = new AndroidJavaClass("android.os.Build"))
                {
                    string serial = buildClass.GetStatic<string>("SERIAL");
                    if (IsUsableIdentifier(serial))
                    {
                        return serial;
                    }

                    try
                    {
                        serial = buildClass.CallStatic<string>("getSerial");
                    }
                    catch
                    {
                        serial = null;
                    }

                    if (IsUsableIdentifier(serial))
                    {
                        return serial;
                    }
                }
            }
            catch
            {
            }
#endif

            return null;
        }

        public static string GetStableIdentifier()
        {
            string serial = GetMxrSerial();
            if (!string.IsNullOrWhiteSpace(serial))
            {
                return serial;
            }

            string androidSerial = GetAndroidSerial();
            if (!string.IsNullOrWhiteSpace(androidSerial))
            {
                return androidSerial;
            }

            string uniqueIdentifier = SystemInfo.deviceUniqueIdentifier;
            if (!string.IsNullOrWhiteSpace(uniqueIdentifier))
            {
                return uniqueIdentifier;
            }

            string deviceName = SystemInfo.deviceName;
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                return deviceName;
            }

            string deviceModel = SystemInfo.deviceModel;
            if (!string.IsNullOrWhiteSpace(deviceModel))
            {
                return deviceModel;
            }

            return Unknown;
        }

        public static string GetDisplayLabel(bool preferDeviceName, string unknownLabel)
        {
            string fallbackLabel = string.IsNullOrWhiteSpace(unknownLabel) ? Unknown : unknownLabel;
            string deviceName = MXRManager.System?.RuntimeSettingsSummary?.deviceName;
            string serial = GetMxrSerial() ?? GetAndroidSerial();
            string systemName = SystemInfo.deviceName;
            string systemModel = SystemInfo.deviceModel;

            if (preferDeviceName)
            {
                return FirstNonEmpty(deviceName, systemName, serial, systemModel, fallbackLabel);
            }

            return FirstNonEmpty(serial, deviceName, systemName, systemModel, fallbackLabel);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return Unknown;
        }

        private static bool IsUsableIdentifier(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, Unknown, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}