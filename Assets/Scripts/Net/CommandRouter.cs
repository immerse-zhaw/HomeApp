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

        public void Init(ProjectSettings s, StateMachine st, VideoController vc, GlbController gc)
        {
            settings = s;
            state = st;
            videoController = vc;
            glbController = gc;
            Debug.Log("[CommandRouter] Initialized.");
        }

        public void Handle(string json)
        {
            Debug.Log($"[CommandRouter] << {json}");
            Envelope env = JsonUtility.FromJson<Envelope>(json);
            switch (env.type)
            {
                case "video.play":
                    {
                        PlayVideoCmd cmd = JsonUtility.FromJson<PlayVideoCmd>(json);
                        string url = settings.WebsiteUrl + cmd.url;
                        videoController.PlayVideo(url, cmd.mapping, cmd.projection, cmd.stereo, cmd.name, cmd.fileId);
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
                case "model.setScale":
                    {
                        ModelSetScaleCmd cmd = JsonUtility.FromJson<ModelSetScaleCmd>(json);
                        glbController.SetScale(cmd.scale);
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
