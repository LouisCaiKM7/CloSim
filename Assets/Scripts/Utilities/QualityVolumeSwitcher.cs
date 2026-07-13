using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Utilities
{
    public class GraphicsVolumeSwitcher : MonoBehaviour
    {
        [Serializable]
        private struct RenderPipelineVolumeProfile
        {
            public RenderPipelineAsset renderPipelineAsset;
            public VolumeProfile volumeProfile;
        }

        [SerializeField] private Volume globalVolume;

        [Header("Volume Profiles by Graphics Settings")]
        [Tooltip("Used when GraphicsSettings.currentRenderPipeline is null, which means the Built-in Render Pipeline is active.")]
        [SerializeField] private VolumeProfile builtInRenderPipelineProfile;
        [SerializeField] private RenderPipelineVolumeProfile[] profilesByRenderPipeline;
        [SerializeField] private VolumeProfile fallbackProfile;

        private RenderPipelineAsset _lastAppliedRenderPipeline;

        private void OnEnable()
        {
            ApplyCurrentGraphicsProfile();
        }

        private void Update()
        {
            if (_lastAppliedRenderPipeline != GraphicsSettings.currentRenderPipeline)
            {
                ApplyCurrentGraphicsProfile();
            }
        }

        public void ApplyCurrentGraphicsProfile()
        {
            if (globalVolume == null)
            {
                Debug.LogWarning("No Global Volume assigned.");
                return;
            }

            RenderPipelineAsset currentPipeline = GraphicsSettings.currentRenderPipeline;
            VolumeProfile profile = GetProfileForRenderPipeline(currentPipeline);

            if (profile == null)
            {
                Debug.LogWarning($"No Volume Profile assigned for active render pipeline '{GetRenderPipelineName(currentPipeline)}'.");
                return;
            }

            globalVolume.profile = profile;
            _lastAppliedRenderPipeline = currentPipeline;
        }

        private VolumeProfile GetProfileForRenderPipeline(RenderPipelineAsset renderPipelineAsset)
        {
            if (renderPipelineAsset == null)
            {
                return builtInRenderPipelineProfile != null ? builtInRenderPipelineProfile : fallbackProfile;
            }

            if (profilesByRenderPipeline != null)
            {
                foreach (RenderPipelineVolumeProfile entry in profilesByRenderPipeline)
                {
                    if (entry.renderPipelineAsset == renderPipelineAsset)
                    {
                        return entry.volumeProfile != null ? entry.volumeProfile : fallbackProfile;
                    }
                }
            }

            return fallbackProfile;
        }

        private static string GetRenderPipelineName(RenderPipelineAsset renderPipelineAsset)
        {
            return renderPipelineAsset != null ? renderPipelineAsset.name : "Built-in Render Pipeline";
        }
    }
}
