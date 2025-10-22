using System;

namespace Net.Messages
{
    [Serializable]
    public class HelloMsg
    {
        public string type = "hello";
        public DeviceInfo device = new DeviceInfo();
        public AppInfo app = new AppInfo();
    }

    [Serializable]
    public class DeviceInfo
    {
        public string androidId;
        public string model;
    }

    [Serializable]
    public class AppInfo
    {
        public string name;
        public string version;
    }

    [Serializable]
    public class Envelope
    {
        public string type;
    }

    [Serializable]
    public class PlayVideoCmd
    {
        public string type = "video.play";
        public string url;
        public string projection = "360"; // "360" | "180" | "2d"
        public string stereo = "mono";    // "mono" | "tb" | "sbs"
        public float startTime = 0f;
    }

    [Serializable]
    public class PauseVideoCmd
    {
        public string type = "video.pause";
    }

    [Serializable]
    public class ResumeVideoCmd
    {
        public string type = "video.resume";
    }

    [Serializable]
    public class StopVideoCmd
    {
        public string type = "video.stop";
    }

    [Serializable]
    public class LoadModelCmd
    {
        public string type = "model.load";
        public string url;
    }

    [Serializable]
    public class ClearModelCmd
    {
        public string type = "model.close";
    }
}
