using Cysharp.Threading.Tasks;
using Data;
using Fusion;
using UnityEngine;

namespace Manager
{
    public class EnvironmentEffectManager : NetworkBehaviour
    {
        [Header("Weather Effects")] 
        public ParticleSystem rainEffect;
        public ParticleSystem snowEffect;
        public AudioSource windAudio;
        public AudioSource indoorAmbient;

        [Header("Lighting")] 
        public Light globalLight;
        public float indoorLightIntensity = 0.7f;
        public float outdoorLightIntensity = 1.0f;

        private bool _isPlayerIndoor = false;
        private WallRoom _currentRoom = null;
        
        public static EnvironmentEffectManager Instance { get; private set; }
        
        public override void Spawned()
        {
            if (!HasStateAuthority) return;

            Instance = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (!hasState) return;
			
            Destroy(Instance);
        }
        
        public void UpdateEnvironmentEffects(bool isIndoor, WallRoom roomData = null)
        {
            // 1. 목표 값 설정 (삼항 연산자 사용)
            float windVolume = isIndoor ? 0.1f : 1.0f;
            float ambientVolume = isIndoor ? 0.5f : 0f;
            float lightIntensity;

            if (isIndoor)
            {
                // 실내 조명: roomData의 lightLevel을 기반으로 계산
                lightIntensity = roomData != null
                    ? Mathf.Lerp(0.3f, indoorLightIntensity, roomData.lightLevel)
                    : indoorLightIntensity; // 혹시 모를 null 대비
            }
            else
            {
                // 실외 조명
                lightIntensity = outdoorLightIntensity;
            }

            // 2. 날씨 효과 제어
            // 참고: 실제로는 WeatherManager 같은 별도의 시스템에서 현재 날씨를 확인해야 합니다.
            bool isRaining = false; /* WeatherManager.IsRaining; */
            bool isSnowing = false; /* WeatherManager.IsSnowing; */

            if (rainEffect != null) rainEffect.gameObject.SetActive(!isIndoor && isRaining);
            if (snowEffect != null) snowEffect.gameObject.SetActive(!isIndoor && isSnowing);

            // 3. 설정된 목표 값으로 효과 적용 (기존 로직 재사용)
            SmoothAudioTransition(windAudio, windVolume, 1.0f).Forget();
            SmoothAudioTransition(indoorAmbient, ambientVolume, 1.0f).Forget();
            SmoothLightTransition(lightIntensity, 2.0f).Forget();
        }

        /// <summary>
        /// 오디오 볼륨을 부드럽게 변경합니다.
        /// </summary>
        private async UniTask SmoothAudioTransition(AudioSource audio, float targetVolume, float duration)
        {
            if (audio == null) return;

            float startVolume = audio.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                audio.volume = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
                // 다음 프레임까지 대기합니다.
                await UniTask.NextFrame();
            }

            audio.volume = targetVolume;
        }

        /// <summary>
        /// 전역 조명 강도를 부드럽게 변경합니다.
        /// </summary>
        private async UniTask SmoothLightTransition(float targetIntensity, float duration)
        {
            if (globalLight == null) return;

            float startIntensity = globalLight.intensity;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float currentIntensity = Mathf.Lerp(startIntensity, targetIntensity, elapsed / duration);
                globalLight.intensity = currentIntensity;
                RenderSettings.ambientIntensity = currentIntensity;
                // 다음 프레임까지 대기합니다.
                await UniTask.NextFrame();
            }

            globalLight.intensity = targetIntensity;
            RenderSettings.ambientIntensity = targetIntensity;
        }
    }

}