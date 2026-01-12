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
        public string serial;
        public string model;
        public string systemStatus;
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
    public class Home
    {
        public string type = "home";
    }

    [Serializable]
    public class PlayVideoCmd
    {
        public string type = "video.play";
        public string name;
        public string url;
        public string fileId;
        public string mapping = "equirectangular"; // "equirectangular" | "cubemap"
        public string projection = "360"; // "360" | "180"
        public string stereo = "mono";    // "mono" | "tb" | "sbs"
    }

    [Serializable]
    public class ChangeMappingVideoCmd
    {
        public string type = "video.changeMapping";
        public string mapping = "equirectangular"; // "equirectangular" | "cubemap"
        public string projection = "360"; // "360" | "180"
        public string stereo = "mono";    // "mono" | "tb" | "sbs"
    }

    [Serializable]
    public class SeekVideoCmd
    {
        public string type = "video.seek";
        public double timeCode = 0.0;
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
        public string name;
        public string url;
        public string fileId;
    }

    [Serializable]
    public class ClearModelCmd
    {
        public string type = "model.close";
    }

    [Serializable]
    public class ModelPlayAnimationCmd
    {
        public string type = "model.playAnimation";
        public string name;
    }

    [Serializable]
    public class ModelStopAnimationCmd
    {
        public string type = "model.stopAnimation";
    }

    [Serializable]
    public class ModelSetPointCmd
    {
        public string type = "model.setPointSize";
        public float size;
    }

}