#region License
/*
Microsoft Public License (Ms-PL)
MonoGame - Copyright © 2009 The MonoGame Team

All rights reserved.

This license governs use of the accompanying software. If you use the software, you accept this license. If you do not
accept the license, do not use the software.

1. Definitions
The terms "reproduce," "reproduction," "derivative works," and "distribution" have the same meaning here as under 
U.S. copyright law.

A "contribution" is the original software, or any additions or changes to the software.
A "contributor" is any person that distributes its contribution under this license.
"Licensed patents" are a contributor's patent claims that read directly on its contribution.

2. Grant of Rights
(A) Copyright Grant- Subject to the terms of this license, including the license conditions and limitations in section 3, 
each contributor grants you a non-exclusive, worldwide, royalty-free copyright license to reproduce its contribution, prepare derivative works of its contribution, and distribute its contribution or any derivative works that you create.
(B) Patent Grant- Subject to the terms of this license, including the license conditions and limitations in section 3, 
each contributor grants you a non-exclusive, worldwide, royalty-free license under its licensed patents to make, have made, use, sell, offer for sale, import, and/or otherwise dispose of its contribution in the software or derivative works of the contribution in the software.

3. Conditions and Limitations
(A) No Trademark License- This license does not grant you rights to use any contributors' name, logo, or trademarks.
(B) If you bring a patent claim against any contributor over patents that you claim are infringed by the software, 
your patent license from such contributor to the software ends automatically.
(C) If you distribute any portion of the software, you must retain all copyright, patent, trademark, and attribution 
notices that are present in the software.
(D) If you distribute any portion of the software in source code form, you may do so only under this license by including 
a complete copy of this license with your distribution. If you distribute any portion of the software in compiled or object 
code form, you may only do so under a license that complies with this license.
(E) The software is licensed "as-is." You bear the risk of using it. The contributors give no express warranties, guarantees
or conditions. You may have additional consumer rights under your local laws which this license cannot change. To the extent
permitted under your local laws, the contributors exclude the implied warranties of merchantability, fitness for a particular
purpose and non-infringement.
*/
#endregion License
﻿
using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Xna;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

#if DIRECTX
using SharpDX;
using SharpDX.XAudio2;
using SharpDX.Multimedia;
using SharpDX.X3DAudio;
#elif (WINDOWS && OPENGL) || LINUX
using OpenTK.Audio.OpenAL;
#endif

namespace Microsoft.Xna.Framework.Audio
{
    public sealed class SoundEffect : IDisposable
    {
        private bool isDisposed = false;

        #region Internal Audio Data

        private string _name;

#if DIRECTX
        internal DataStream _dataStream;
        internal AudioBuffer _buffer;
        internal AudioBuffer _loopedBuffer;
        internal WaveFormat _format;
        
        // These three fields are used for keeping track of instances created
        // internally when Play is called directly on SoundEffect.
        private List<SoundEffectInstance> _playingInstances;
        private List<SoundEffectInstance> _availableInstances;
        private List<SoundEffectInstance> _toBeRecycledInstances;
#else
        private string _filename = "";
        internal byte[] _data;

#if (WINDOWS && OPENGL) || LINUX

        // OpenAL-specific information

        internal int Size
        {
            get;
            set;
        }

        internal float Rate
        {
            get;
            set;
        }

        internal ALFormat Format
        {
            get;
            set;
        }
#else
        private Sound _sound;
        private SoundEffectInstance _instance;
#endif

#endif

        #endregion

        #region Internal Constructors

#if DIRECTX
        internal SoundEffect()
        {
        }

        // Extended constructor which supports custom formats / compression.
        internal SoundEffect(WaveFormat format, byte[] buffer, int offset, int count, int loopStart, int loopLength)
        {
            Initialize(format, buffer, offset, count, loopStart, loopLength);
        }

#else
        internal SoundEffect(string fileName)
        {
            _filename = fileName;

            if (_filename == string.Empty )
            {
                throw new FileNotFoundException("Supported Sound Effect formats are wav, mp3, acc, aiff");
            }

            _name = Path.GetFileNameWithoutExtension(fileName);

#if (WINDOWS && OPENGL) || LINUX
            Stream s;
            try
            {
                s = File.OpenRead(fileName);
            }
            catch (IOException e)
            {
                throw new Content.ContentLoadException("Could not load audio data", e);
            }

            _data = LoadAudioStream(s, 1.0f, false);
            s.Close();
#else
            _sound = new Sound(_filename, 1.0f, false);
#endif
        }

        //SoundEffect from playable audio data
        internal SoundEffect(string name, byte[] data)
        {
            _data = data;
            _name = name;

#if (WINDOWS && OPENGL) || LINUX
            Stream s;
            try
            {
                s = new MemoryStream(data);
            }
            catch (IOException e)
            {
                throw new Content.ContentLoadException("Could not load audio data", e);
            }
            _data = LoadAudioStream(s, 1.0f, false);
            s.Close();
#else
            _sound = new Sound(_data, 1.0f, false);
#endif
        }        
#endif

        internal SoundEffect(Stream s)
        {
#if (WINDOWS && OPENGL) || LINUX
            _data = LoadAudioStream(s, 1.0f, false);
#elif !DIRECTX
            var data = new byte[s.Length];
            s.Read(data, 0, (int)s.Length);

            _data = data;
            _sound = new Sound(_data, 1.0f, false);
#endif
        }

        internal SoundEffect(string name, byte[] buffer, int sampleRate, AudioChannels channels)
            : this(buffer, sampleRate, channels)
        {
            _name = name;
        }

        #endregion

        #region Public Constructors

        public SoundEffect(byte[] buffer, int sampleRate, AudioChannels channels)
        {
#if DIRECTX            
            Initialize(new WaveFormat(sampleRate, (int)channels), buffer, 0, buffer.Length, 0, buffer.Length);
#elif (WINDOWS && OPENGL) || LINUX
            _data = buffer;
            Size = buffer.Length;
            Format = (channels == AudioChannels.Stereo) ? ALFormat.Stereo16 : ALFormat.Mono16;
            Rate = sampleRate;
#else
            //buffer should contain 16-bit PCM wave data
            short bitsPerSample = 16;

            _name = "";

            using (var mStream = new MemoryStream(44+buffer.Length))
            using (var writer = new BinaryWriter(mStream))
            {
                writer.Write("RIFF".ToCharArray()); //chunk id
                writer.Write((int)(36 + buffer.Length)); //chunk size
                writer.Write("WAVE".ToCharArray()); //RIFF type

                writer.Write("fmt ".ToCharArray()); //chunk id
                writer.Write((int)16); //format header size
                writer.Write((short)1); //format (PCM)
                writer.Write((short)channels);
                writer.Write((int)sampleRate);
                short blockAlign = (short)((bitsPerSample / 8) * (int)channels);
                writer.Write((int)(sampleRate * blockAlign)); //byte rate
                writer.Write((short)blockAlign);
                writer.Write((short)bitsPerSample);

                writer.Write("data".ToCharArray()); //chunk id
                writer.Write((int)buffer.Length); //data size   MonoGame.Framework.Windows8.DLL!Microsoft.Xna.Framework.Audio.Sound.Sound(byte[] audiodata, float volume, bool looping) Line 199    C#

                writer.Write(buffer);

                _data = mStream.ToArray();
            }

            _sound = new Sound(_data, 1.0f, false);
#endif
        }

        public SoundEffect(byte[] buffer, int offset, int count, int sampleRate, AudioChannels channels, int loopStart, int loopLength)
        {
#if DIRECTX
            Initialize(new WaveFormat(sampleRate, (int)channels), buffer, offset, count, loopStart, loopLength);
#else
            throw new NotImplementedException();
#endif
        }

        #endregion

        #region Additional SoundEffect/SoundEffectInstance Creation Methods

        public SoundEffectInstance CreateInstance()
        {
#if DIRECTX
            SourceVoice voice = null;
            if (Device != null)
                voice = new SourceVoice(Device, _format, VoiceFlags.None, XAudio2.MaximumFrequencyRatio);

            var instance = new SoundEffectInstance(this, voice);
#elif (WINDOWS && OPENGL) || LINUX
            var instance = new SoundEffectInstance(this);
#else
            var instance = new SoundEffectInstance();
            instance.Sound = _sound;
#endif
            return instance;
        }

        public static SoundEffect FromStream(Stream stream)
        {            
#if ANDROID
            throw new NotImplementedException();
#else
            return new SoundEffect(stream);
#endif
        }

        #endregion

        #region Play

        public bool Play()
        {
#if (WINDOWS && OPENGL) || LINUX
            return Play(MasterVolume, 0.0f, 0.0f);
#else
            return Play(1.0f, 0.0f, 0.0f);
#endif
        }

        public bool Play(float volume, float pitch, float pan)
        {
#if DIRECTX
            if (MasterVolume > 0.0f)
            {
                if (_playingInstances == null)
                {
                    // Allocate lists first time we need them.
                    _playingInstances = new List<SoundEffectInstance>();
                    _availableInstances = new List<SoundEffectInstance>();
                    _toBeRecycledInstances = new List<SoundEffectInstance>();
                }
                else
                {
                    // Cleanup instances which have finished playing.                    
                    foreach (var inst in _playingInstances)
                    {
                        if (inst.State == SoundState.Stopped)
                        {
                            _toBeRecycledInstances.Add(inst);
                        }
                    }                    
                }

                // Locate a SoundEffectInstance either one already
                // allocated and not in use or allocate a new one.
                SoundEffectInstance instance = null;
                if (_toBeRecycledInstances.Count > 0)
                {
                    foreach (var inst in _toBeRecycledInstances)
                    {
                        _availableInstances.Add(inst);
                        _playingInstances.Remove(inst);
                    }
                    _toBeRecycledInstances.Clear();
                }
                if (_availableInstances.Count > 0)
                {
                    instance = _availableInstances[0];
                    _playingInstances.Add(instance);
                    _availableInstances.Remove(instance);
                }
                else
                {
                    instance = CreateInstance();
                    _playingInstances.Add(instance);
                }

                instance.Volume = volume;
                instance.Pitch = pitch;
                instance.Pan = pan;
                instance.Play();
            }

            // XNA documentation says this method returns false if the sound limit
            // has been reached. However, there is no limit on PC.
            return true;
#elif (WINDOWS && OPENGL) || LINUX
            if (MasterVolume > 0.0f)
            {
                SoundEffectInstance instance = CreateInstance();
                instance.Volume = volume;
                instance.Pitch = pitch;
                instance.Pan = pan;
                instance.Play();
            }
            return false;
#else
            if ( MasterVolume > 0.0f )
            {
                if(_instance == null)
                    _instance = CreateInstance();
                _instance.Volume = volume;
                _instance.Pitch = pitch;
                _instance.Pan = pan;
                _instance.Play();
                return _instance.Sound.Playing;
            }
            return false;
#endif
        }

        #endregion

        #region Public Properties

#if (WINDOWS && OPENGL) || LINUX
        private TimeSpan _duration = TimeSpan.Zero;
#endif

        public TimeSpan Duration
        {
            get
            {
#if DIRECTX                    
                var sampleCount = _buffer.PlayLength;
                var avgBPS = _format.AverageBytesPerSecond;
                
                return TimeSpan.FromSeconds((float)sampleCount / (float)avgBPS);
#elif (WINDOWS && OPENGL) || LINUX
                return _duration;
#else
                if ( _sound != null )
                {
                    return new TimeSpan(0,0,(int)_sound.Duration);
                }
                else
                {
                    return new TimeSpan(0);
                }
#endif
            }
        }

        public string Name
        {
            get
            {
                return _name;
            }
            set 
            {
                _name = value;
            }
        }

        #endregion

        #region Static Members

        static float _masterVolume = 1.0f;
        public static float MasterVolume 
        { 
            get
            {
                return _masterVolume;
            }
            set
            {
                if (_masterVolume != value)
                {
                    _masterVolume = value;
                }
#if DIRECTX
                MasterVoice.SetVolume(_masterVolume, 0);
#endif
            }
        }

        static float _distanceScale = 1.0f;
        public static float DistanceScale
        {
            get
            {
                return _distanceScale;
            }
            set
            {
                if (value <= 0f)
                {
                    throw new ArgumentOutOfRangeException ("value of DistanceScale");
                }
                _distanceScale = value;
            }
        }

        static float _dopplerScale = 1f;
        public static float DopplerScale
        {
            get
            {
                return _dopplerScale;
            }
            set
            {
                // As per documenation it does not look like the value can be less than 0
                //   although the documentation does not say it throws an error we will anyway
                //   just so it is like the DistanceScale
                if (value < 0f)
                {
                    throw new ArgumentOutOfRangeException ("value of DopplerScale");
                }
                _dopplerScale = value;
            }
        }

        static float speedOfSound = 343.5f;
        public static float SpeedOfSound
        {
            get
            {
                return speedOfSound;
            }
            set
            {
                speedOfSound = value;
            }
        }

        #endregion

        #region IDisposable Members

        public bool IsDisposed
        {
            get
            {
                return isDisposed;
            }
        }

        public void Dispose()
        {
#if (WINDOWS && OPENGL) || LINUX
            // No-op. Note that isDisposed remains false!
#else

#if DIRECTX
            _dataStream.Dispose();
#else
            _sound.Dispose();
#endif
            isDisposed = true;

#endif
        }

        #endregion

        #region Additional OpenTK SoundEffect Code

        byte[] LoadAudioStream(Stream s, float volume, bool looping)
        {
            ALFormat format;
            int size;
            int freq;
            byte[] data;

            data = AudioLoader.Load(s, out format, out size, out freq);

            Format = format;
            Size = size;
            Rate = freq;
            return data;
        }

        #endregion

        #region Additional DirectX SoundEffect Code

#if DIRECTX
        internal static XAudio2 Device { get; private set; }
        internal static MasteringVoice MasterVoice { get; private set; }

        private static bool _device3DDirty = true;
        private static Speakers _speakers = Speakers.Stereo;

        // XNA does not expose this, but it exists in X3DAudio.
        [CLSCompliant(false)]
        public static Speakers Speakers
        {
            get
            {
                return _speakers;
            }

            set
            {
                if (_speakers != value)
                {
                    _speakers = value;
                    _device3DDirty = true;
                }
            }
        }

        private static X3DAudio _device3D;

        internal static X3DAudio Device3D
        {
            get
            {
                if (_device3DDirty)
                {
                    _device3DDirty = false;
                    _device3D = new X3DAudio(_speakers);
                }

                return _device3D;
            }
        }

        internal static void InitializeSoundEffect()
        {
            var flags = XAudio2Flags.None;

#if !WINRT && DEBUG
            flags |= XAudio2Flags.DebugEngine;
#endif
            try
            {
                if (Device == null)
                {
                    // This cannot fail.
                    Device = new XAudio2(flags, ProcessorSpecifier.DefaultProcessor);
                    Device.StartEngine();
                }

                // Just use the default device.
#if WINRT
                string deviceId = null;
#else
                const int deviceId = 0;
#endif

                if (MasterVoice == null)
                {
                    // Let windows autodetect number of channels and sample rate.
                    MasterVoice = new MasteringVoice(Device, XAudio2.DefaultChannels, XAudio2.DefaultSampleRate, deviceId);
                    MasterVoice.SetVolume(_masterVolume, 0);
                }

                // The autodetected value of MasterVoice.ChannelMask corresponds to the speaker layout.
#if WINRT
                Speakers = (Speakers)MasterVoice.ChannelMask;
#else
                var deviceDetails = Device.GetDeviceDetails(deviceId);
                Speakers = deviceDetails.OutputFormat.ChannelMask;
#endif
            }
            catch
            {
                // Release the device and null it as
                // we have no audio support.
                if (Device != null)
                {
                    Device.Dispose();
                    Device = null;
                }

                MasterVoice = null;
            }
        }

        private void Initialize(WaveFormat format, byte[] buffer, int offset, int count, int loopStart, int loopLength)
        {
            _format = format;

            _dataStream = DataStream.Create<byte>(buffer, true, false);

            // Use the loopStart and loopLength also as the range
            // when playing this SoundEffect a single time / unlooped.
            _buffer = new AudioBuffer()
            {
                Stream = _dataStream,
                AudioBytes = count,
                Flags = BufferFlags.EndOfStream,
                PlayBegin = loopStart,
                PlayLength = loopLength,
                Context = new IntPtr(42),
            };

            _loopedBuffer = new AudioBuffer()
            {
                Stream = _dataStream,
                AudioBytes = count,
                Flags = BufferFlags.EndOfStream,
                LoopBegin = loopStart,
                LoopLength = loopLength,
                LoopCount = AudioBuffer.LoopInfinite,
                Context = new IntPtr(42),
            };            
        }

        static SoundEffect()
        {
            InitializeSoundEffect();
        }

        // Does someone actually need to call this if it only happens when the whole
        // game closes? And if so, who would make the call?
        internal static void Shutdown()
        {
            if (MasterVoice != null)
            {
                MasterVoice.DestroyVoice();
                MasterVoice.Dispose();
                MasterVoice = null;
            }

            if (Device != null)
            {
                Device.StopEngine();
                Device.Dispose();
                Device = null;
            }

            _device3DDirty = true;
            _speakers = Speakers.Stereo;
        }
#endif
        #endregion
    }
}

