using SmallTown.Simulation;
using SmallTown.Simulation.World;
using UnityEngine;

namespace SmallTown.UI
{
    /// <summary>
    /// Procedural ambience (off by default): soft wind/town hum, rain noise, birds by day,
    /// crickets at night. Generated sample-by-sample, no audio assets.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AmbientAudio : MonoBehaviour
    {
        private AudioSource _src;
        private volatile float _rain, _night, _storm, _gain;
        private float _targetGain;
        private uint _seed = 22222;
        private float _brown, _rainLp;
        private double _phase, _chirpPhase, _cricketPhase;
        private int _chirpLeft, _sampleRate = 44100;
        private float _chirpFreq;

        public bool Enabled
        {
            get => _targetGain > 0f;
            set
            {
                _targetGain = value ? 0.35f : 0f;
                if (value && !_src.isPlaying) _src.Play();
            }
        }

        private void Awake()
        {
            _src = GetComponent<AudioSource>();
            _sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            _src.clip = AudioClip.Create("Ambience", _sampleRate * 2, 1, _sampleRate, true, OnRead);
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _src.volume = 1f;
        }

        public void SetState(TownSimulation sim, float night)
        {
            var w = sim.World.Weather;
            _rain = w == Weather.Rain || w == Weather.Storm ? 1f : 0f;
            _storm = w == Weather.Storm ? 1f : 0f;
            _night = night;
            _gain = Mathf.MoveTowards(_gain, _targetGain, Time.unscaledDeltaTime * 0.4f);
            if (_gain <= 0f && _targetGain <= 0f && _src.isPlaying) _src.Stop();
        }

        private float Rand()
        {
            _seed ^= _seed << 13;
            _seed ^= _seed >> 17;
            _seed ^= _seed << 5;
            return (_seed & 0xFFFFFF) / 16777215f * 2f - 1f;
        }

        private void OnRead(float[] data)
        {
            float gain = _gain, rain = _rain, night = _night, storm = _storm;
            for (int i = 0; i < data.Length; i++)
            {
                float white = Rand();
                _brown = Mathf.Clamp(_brown + white * 0.02f, -1f, 1f) * 0.998f;
                float wind = _brown * (0.5f + storm * 0.8f);
                _rainLp += (white - _rainLp) * 0.35f;
                float rainNoise = _rainLp * 0.45f * rain;
                float s = wind * 0.6f + rainNoise;
                if (night < 0.5f && rain < 0.5f)
                {
                    if (_chirpLeft <= 0 && Rand() > 0.99997f)
                    {
                        _chirpLeft = (int)(_sampleRate * 0.12f);
                        _chirpFreq = 2800f + Rand() * 900f;
                    }
                    if (_chirpLeft > 0)
                    {
                        float env = Mathf.Sin(Mathf.PI * (1f - _chirpLeft / (_sampleRate * 0.12f)));
                        _chirpPhase += (_chirpFreq + _chirpLeft * 0.2f) / _sampleRate;
                        s += Mathf.Sin((float)(_chirpPhase * 2.0 * System.Math.PI)) * env * 0.12f;
                        _chirpLeft--;
                    }
                }
                else if (rain < 0.5f)
                {
                    _cricketPhase += 4300.0 / _sampleRate;
                    _phase += 14.0 / _sampleRate;
                    float pulse = Mathf.Max(0f, Mathf.Sin((float)(_phase * 2.0 * System.Math.PI))) ;
                    s += Mathf.Sin((float)(_cricketPhase * 2.0 * System.Math.PI)) * pulse * pulse * 0.035f;
                }
                data[i] = s * gain;
            }
        }
    }
}
