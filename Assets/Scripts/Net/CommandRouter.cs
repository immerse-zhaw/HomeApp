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
                        videoController.PlayVideo(cmd.url, cmd.projection, cmd.stereo, cmd.startTime);
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
                case "video.stop":
                    {
                        videoController.StopVideo();
                        break;
                    }
                case "model.load":
                    {
                        LoadModelCmd cmd = JsonUtility.FromJson<LoadModelCmd>(json);
                        glbController.LoadModel(cmd.url);
                        break;
                    }
                case "model.close":
                    {
                        glbController.CloseModel();
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
