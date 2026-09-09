using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TradeWinds
{
    // Local presentation: observing replicated poses never applies gameplay forces.
    [DefaultExecutionOrder(100)]
    public sealed class VoyageFeedback : MonoBehaviour
    {
        private Camera view;
        private DeckPlayer player;
        private PickableItem[] cargo;
        private bool[] cargoWet;
        private bool playerWet;
        private Vector3 previousFeet;
        private float stride;
        private AudioClip splash, wood, sand, impact;
        private readonly AudioSource[] voices = new AudioSource[12];
        private readonly AudioLowPassFilter[] filters = new AudioLowPassFilter[12];
        private readonly ParticleSystem[] splashes = new ParticleSystem[6];
        private int voiceIndex, splashIndex;
        private Volume volume;
        private VolumeProfile profile;
        private Material splashMaterial;
        public bool Underwater { get; private set; }

        public void Initialize(Camera camera, DeckPlayer owner, PickableItem[] items)
        {
            view = camera; player = owner; cargo = items; cargoWet = new bool[items.Length];
            previousFeet = player.Aboard ? player.DeckPosition : player.WorldPosition;
            if (view.GetComponent<AudioListener>() == null) view.gameObject.AddComponent<AudioListener>();
            view.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            volume = new GameObject("Underwater volume").AddComponent<Volume>();
            volume.transform.SetParent(transform, false); volume.isGlobal = true; volume.priority = 100; volume.weight = 0;
            profile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = profile;
            var color = profile.Add<ColorAdjustments>(); color.colorFilter.Override(new Color(0.24f, 0.58f, 0.95f)); color.saturation.Override(-22);
            var vignette = profile.Add<Vignette>(); vignette.intensity.Override(0.38f); vignette.smoothness.Override(0.75f);
            splash = MakeClip("Water entry", 0.65f, 85, 0.85f, 5);
            wood = MakeClip("Wood footstep", 0.18f, 155, 0.28f, 20);
            sand = MakeClip("Sand footstep", 0.22f, 45, 0.85f, 17);
            impact = MakeClip("Wooden crate impact", 0.4f, 95, 0.42f, 12);
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = new GameObject("Environment voice " + i).AddComponent<AudioSource>();
                voices[i].transform.SetParent(transform, false); voices[i].playOnAwake = false;
                voices[i].spatialBlend = 1; voices[i].minDistance = 2; voices[i].maxDistance = 45;
                voices[i].rolloffMode = AudioRolloffMode.Linear;
                filters[i] = voices[i].gameObject.AddComponent<AudioLowPassFilter>(); filters[i].cutoffFrequency = 22000;
            }
            splashMaterial = new Material(Resources.Load<Material>("Splash"));
            for (int i = 0; i < splashes.Length; i++)
            {
                var particles = new GameObject("Splash pool " + i).AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.transform.SetParent(transform, false);
                var main = particles.main; main.loop = false; main.playOnAwake = false; main.duration = 1;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f); main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.09f); main.gravityModifier = 0.6f; main.maxParticles = 48;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = particles.emission; emission.enabled = false;
                var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55; shape.radius = 0.35f;
                particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = splashMaterial;
                splashes[i] = particles;
            }
            foreach (var item in cargo) item.Impact += CargoImpact;
        }

        private void LateUpdate()
        {
            if (player == null) return;
            Underwater = view.transform.position.y < WaterZone.Surface;
            volume.weight = Mathf.MoveTowards(volume.weight, Underwater ? 1 : 0, Time.unscaledDeltaTime * 5);
            foreach (var filter in filters) filter.cutoffFrequency = Mathf.Lerp(22000, 750, volume.weight);
            Vector3 feet = player.WorldPosition;
            bool wet = feet.y < -0.15f;
            if (wet && !playerWet) Splash(feet);
            playerWet = wet;
            Vector3 relativeFeet = player.Aboard ? player.DeckPosition : feet;
            float movement = Vector3.Distance(relativeFeet, previousFeet); previousFeet = relativeFeet;
            if (!wet && !player.Climbing && !player.AtHelm && !player.Paused && movement < 0.4f && player.IsGrounded)
            {
                stride += movement;
                if (stride > 1.5f)
                {
                    stride = 0;
                    bool island = Physics.Raycast(feet + Vector3.up * 0.15f, Vector3.down, out RaycastHit hit, 0.5f, LayerMask.GetMask("Island"));
                    Play(island ? sand : wood, feet, 0.4f);
                }
            }
            else stride = 0;
            for (int i = 0; i < cargo.Length; i++)
            {
                if (cargo[i] == null) continue;
                bool inWater = cargo[i].transform.position.y < 0;
                if (inWater && !cargoWet[i]) Splash(cargo[i].transform.position);
                cargoWet[i] = inWater;
            }
        }
        private void Splash(Vector3 position)
        {
            position.y = 0; Play(splash, position, 0.65f);
            var particles = splashes[splashIndex++ % splashes.Length];
            particles.transform.SetPositionAndRotation(position, Quaternion.Euler(-90, 0, 0));
            particles.Emit(36);
        }
        private void CargoImpact(Vector3 position, float strength) { Play(impact, position, Mathf.Clamp01(strength / 8)); }
        public void PlayImpact(Vector3 position, float strength) { CargoImpact(position, strength); }
        private void Play(AudioClip clip, Vector3 position, float gain)
        {
            var source = voices[voiceIndex++ % voices.Length]; source.transform.position = position;
            source.Stop(); source.clip = clip; source.volume = gain; source.Play();
        }
        private static AudioClip MakeClip(string name, float duration, float frequency, float noiseMix, float decay)
        {
            const int rate = 22050;
            var samples = new float[Mathf.CeilToInt(rate * duration)];
            var random = new System.Random(73); float filtered = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                filtered = Mathf.Lerp(filtered, (float)random.NextDouble() * 2 - 1, 0.4f);
                samples[i] = (Mathf.Sin(t * frequency * Mathf.PI * 2) * (1 - noiseMix) + filtered * noiseMix)
                    * Mathf.Exp(-t * decay) * Mathf.Clamp01(t * 500) * 0.8f;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false); clip.SetData(samples, 0); return clip;
        }
        private void OnDestroy()
        {
            if (cargo != null) foreach (var item in cargo) if (item != null) item.Impact -= CargoImpact;
            if (profile != null) { foreach (var component in profile.components) Destroy(component); Destroy(profile); }
            Destroy(splash); Destroy(wood); Destroy(sand); Destroy(impact); Destroy(splashMaterial);
        }
    }
}
