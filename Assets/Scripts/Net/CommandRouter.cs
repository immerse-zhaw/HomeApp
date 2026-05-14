using App;
using Net.Messages;
using Playback;
using UnityEngine;

namespace Net
{
    public class CommandRouter : MonoBehaviour
    {
        private ProjectSettings settings;
        private StateMachine state;
        private VideoController videoController;
        private GlbController glbController;
        private PassthroughController passthroughController;

        public void Init(ProjectSettings s, StateMachine st, VideoController vc, GlbController gc, PassthroughController pc)
        {
            settings = s;
            state = st;
            videoController = vc;
            glbController = gc;
            passthroughController = pc;
            Debug.Log("[CommandRouter] Initialized.");
        }

        public void Handle(string json)
        {
            Envelope env = JsonUtility.FromJson<Envelope>(json);
            switch (env.type)
            {
                case "video.play":
                    {
                        PlayVideoCmd cmd = JsonUtility.FromJson<PlayVideoCmd>(json);
                        string url = settings.WebsiteUrl + cmd.url;
                        videoController.PlayVideo(url, cmd.name, cmd.fileId, autoPlay: false);
                        break;
                    }
                case "video.changeMapping":
                    {
                        ChangeMappingVideoCmd cmd = JsonUtility.FromJson<ChangeMappingVideoCmd>(json);
                        videoController.ChangeProjectionMapping(cmd.mapping, cmd.projection, cmd.stereo);
                        break;
                    }
                case "video.seek":
                    {
                        SeekVideoCmd cmd = JsonUtility.FromJson<SeekVideoCmd>(json);
                        videoController.Seek(cmd.timeCode);
                        break;
                    }
                case "video.pause":
                    {
                        videoController.PauseVideo();
                        break;
                    }
                case "video.resume":
                    {
                        videoController.ResumeVideo();
                        break;
                    }
                case "home":
                    {
                        // Home: ensure all content is closed and return to idle with MXR panel shown.
                        if (videoController != null)
                        {
                            videoController.StopVideo();
                        }
                        if (glbController != null)
                        {
                            glbController.CloseModel();
                        }
                        state?.SetState(AppState.Idle);
                        state?.SetAction("none");
                        state?.ClearContent();
                        break;
                    }
                case "model.load":
                    {
                        LoadModelCmd cmd = JsonUtility.FromJson<LoadModelCmd>(json);
                        string url = settings.WebsiteUrl + cmd.url;
                        glbController.LoadModel(url, cmd.name, cmd.fileId);
                        break;
                    }
                case "model.playAnimation":
                    {
                        ModelPlayAnimationCmd cmd = JsonUtility.FromJson<ModelPlayAnimationCmd>(json);
                        glbController.PlayAnimation(cmd.name);
                        break;
                    }
                case "model.stopAnimation":
                    {
                        _ = JsonUtility.FromJson<ModelStopAnimationCmd>(json);
                        glbController.StopAnimation();
                        break;
                    }
                case "model.setPointSize":
                    {
                        ModelSetPointCmd cmd = JsonUtility.FromJson<ModelSetPointCmd>(json);
                        glbController.SetPointsSize(cmd.size);
                        break;
                    }
                case "passthrough.enable":
                    {
                        _ = JsonUtility.FromJson<PassthroughEnableCmd>(json);
                        passthroughController?.EnablePassthrough("server cmd");
                        break;
                    }
                case "passthrough.disable":
                    {
                        _ = JsonUtility.FromJson<PassthroughDisableCmd>(json);
                        passthroughController?.DisablePassthrough("server cmd");
                        break;
                    }
                default:
                    {
                        break;
                    }
            }
        }

    }
}
