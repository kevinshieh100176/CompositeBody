using System;
using System.Collections;
using UnityEngine;

namespace Unity.Services.Vivox.AudioTaps
{
    internal class VivoxAudioProcessor
    {
        private readonly VivoxAudioTap m_owner;
        private int m_ownerTapId = -1;
        private AudioConfiguration m_cachedAudioConfig;
        private AudioSource m_audioSource;
        private AudioClip m_streamClip;
        private const double m_internalAudioPeriodDuration = 0.02;
        private int m_internalAudioPeriodFrames;
        private int m_streamClipTotalFramesSize;
        private int m_writePointer;
        private int m_lastReadPointer;
        private int m_readPointer;
        private int m_minLatencyFrames;
        private int m_maxLatencyFrames;
        private float[] m_internalBuffer;
        private float[] m_silenceBuffer;
        private float[] m_silenceBufferForAudioPeriod;
        private const int m_assumedVivoxSampleRate = 48000;
        private int m_missedReads = 0;
        private const int m_missedReadsLimit = 20;
        private double m_lastDspTime = 0.0;
        private int m_BufferedAudio = 0;

        protected readonly int VxErrorNoMoreData = VivoxCoreInstancePINVOKE.VxErrorNoMoreData_get();

        public VivoxAudioProcessor(VivoxAudioTap owner)
        {
            m_owner = owner;
        }

        public bool IsReady()
        {
            if (m_cachedAudioConfig.dspBufferSize <= 0 || m_cachedAudioConfig.sampleRate <= 0)
            {
                return false;
            }

            return true;
        }

        public void InitializeAudioConfiguration()
        {
            // Register for Audio Configuration changes and store a copy of the AudioConfiguration
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            m_cachedAudioConfig = AudioSettings.GetConfiguration();
            VivoxLogger.LogVerbose($"{m_cachedAudioConfig.sampleRate} {m_cachedAudioConfig.dspBufferSize}");
        }

        public void UninitializeAudioConfiguration()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            m_cachedAudioConfig = AudioSettings.GetConfiguration();
            VivoxLogger.LogVerbose($"OnAudioConfigurationChanged: {m_cachedAudioConfig.dspBufferSize} {m_cachedAudioConfig.sampleRate}");
            InitializeResources(m_ownerTapId);
        }

        public void InitializeResources(int tapId)
        {
            m_ownerTapId = tapId;
            m_lastReadPointer = 0;
            m_readPointer = 0;
            m_writePointer = 0;
            m_lastDspTime = AudioSettings.dspTime;

            // Grab reference to the Audio Source component that must exist due to the RequireComponent attribute above
            m_audioSource = m_owner.gameObject.GetComponent<AudioSource>();
            m_audioSource.loop = true;

            m_internalAudioPeriodFrames = (int)Math.Ceiling(m_internalAudioPeriodDuration * m_cachedAudioConfig.sampleRate);

            // For Coroutine implementation
            if (m_cachedAudioConfig.dspBufferSize > 0)
            {
                m_silenceBuffer = new float[m_cachedAudioConfig.dspBufferSize * m_owner.ChannelCount];
                m_streamClipTotalFramesSize = (m_cachedAudioConfig.sampleRate * 3 / m_cachedAudioConfig.dspBufferSize) * m_cachedAudioConfig.dspBufferSize; // The math here makes the buffer size to be nearly 3 seconds, and also an exact multiple of dspBufferSize.
                m_silenceBufferForAudioPeriod = new float[m_internalAudioPeriodFrames * m_owner.ChannelCount];
                m_streamClip = AudioClip.Create("StreamClip" + m_ownerTapId.ToString(), m_streamClipTotalFramesSize, m_owner.ChannelCount, m_cachedAudioConfig.sampleRate, false);
                SilenceAudioClipData(0, m_streamClipTotalFramesSize);
                m_internalBuffer = new float[m_internalAudioPeriodFrames * m_owner.ChannelCount];
            }
            else
            {
                m_silenceBuffer = null;
                m_silenceBufferForAudioPeriod = null;
                m_streamClipTotalFramesSize = 0;
                m_streamClip = null;
                m_internalBuffer = null;
            }

            // Calculate minimum latency in frames
            int minLatencyAudioPeriodMultiplier;
            switch (Application.platform)
            {
                default:
                    minLatencyAudioPeriodMultiplier = 2;
                    break;
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    minLatencyAudioPeriodMultiplier = 4;
                    break;
            }
            m_minLatencyFrames = (int)Math.Max(Math.Round(m_cachedAudioConfig.sampleRate * 0.02 * minLatencyAudioPeriodMultiplier), m_cachedAudioConfig.dspBufferSize); // Some periods of 20 ms audio or one dspBufferSize, whichever is highest

            // Calculate maximum latency in frames
            double secondDivisor = 4.0; // Hard maximum on latency is 0.25 seconds, rounded to a multiple of dspBufferSize. Unless...
            if (m_cachedAudioConfig.sampleRate <= 8000 && m_cachedAudioConfig.dspBufferSize >= 1024)
            {
                secondDivisor = 2.0; // ... allow for a whole half second of latency when sampleRate and dspBufferSize are less than ideal.
            }
            m_maxLatencyFrames = (int)Math.Round(m_cachedAudioConfig.sampleRate / secondDivisor / m_cachedAudioConfig.dspBufferSize) * m_cachedAudioConfig.dspBufferSize;

            VivoxLogger.LogVerbose($"Min latency is {m_minLatencyFrames} frames ({(float)m_minLatencyFrames/(float)m_cachedAudioConfig.sampleRate} seconds), max latency is {m_maxLatencyFrames} frames ({(float)m_maxLatencyFrames/(float)m_cachedAudioConfig.sampleRate} seconds)");
            VivoxLogger.LogVerbose($"Meaning latency ({(float)m_minLatencyFrames / 48000f} seconds), ({(float)m_maxLatencyFrames / 48000f} seconds)");

            // Set clip and play
            m_audioSource.clip = m_streamClip;
            if (m_owner.gameObject.activeInHierarchy && m_audioSource.enabled)
            {
                m_audioSource.Play();
            }
        }

        public void Stop()
        {
            if (m_audioSource != null)
            {
                m_audioSource.clip = null;
            }
        }

        public float ProcessAudio()
        {
            m_readPointer = m_audioSource.timeSamples;
            double dspDifference = AudioSettings.dspTime - m_lastDspTime;
            // if (dspDifference > 0.025f)
            // {
            //     VivoxLogger.LogVerbose("DspDifference = " + dspDifference.ToString());
            // }

            if (((m_readPointer - m_lastReadPointer) % (m_cachedAudioConfig.dspBufferSize)) != 0)
            {
                VivoxLogger.LogVerbose("Read step difference doesn't agree with dspBufferSize");
            }

            // Clear the frames in the AudioClip that we can assume have been played
            SilenceAudioClipData(m_lastReadPointer, m_readPointer);

            // Minimum latency condition
            if ((m_writePointer - m_readPointer < m_cachedAudioConfig.dspBufferSize) && (m_writePointer - m_readPointer > -(m_streamClipTotalFramesSize - m_maxLatencyFrames * 3)) || (m_writePointer < m_readPointer) && ((m_streamClipTotalFramesSize - m_readPointer + m_writePointer) < m_cachedAudioConfig.dspBufferSize))
            {
                var writePointerBef = m_writePointer;
                // Sets the write pointer to the minimum latency because read and write were within a dspBufferSize away from each other
                m_writePointer = (m_readPointer + m_minLatencyFrames) % m_streamClipTotalFramesSize;
                SilenceAudioClipData(m_readPointer, m_writePointer);
                VivoxLogger.LogVerbose(DateTime.Now.ToShortTimeString() + " Underrun, writer pointer bumped forward");
                VivoxLogger.LogVerbose(
                    $"Detected underrun with write pointer {writePointerBef} and read pointer {m_readPointer}, write pointer set to {m_writePointer}");
            }


            int result = 0;
            float maxDataOut = 0;

            while (result == 0)
            {
                // Maximum latency condition
                if (m_writePointer - m_readPointer > m_maxLatencyFrames || (m_writePointer < m_readPointer && (m_streamClipTotalFramesSize - m_readPointer + m_writePointer) > m_maxLatencyFrames))
                {
                    var readPointerBef = m_readPointer;
                    // Move read pointer to two minimum latencies behind the write pointer
                    int newPointer = (m_writePointer - m_minLatencyFrames * 2) % m_streamClipTotalFramesSize;
                    if (newPointer < 0)
                    {
                        newPointer += m_streamClipTotalFramesSize;
                    }
                    // Round new pointer down to the nearest multiple of dspBufferSize
                    newPointer = (newPointer / m_cachedAudioConfig.dspBufferSize) * m_cachedAudioConfig.dspBufferSize;
                    // Silence audio that was jumped over
                    SilenceAudioClipData(m_readPointer, newPointer);

                    // Update readpointer and AudioSource playback point
                    m_audioSource.timeSamples = m_readPointer = newPointer;
                    VivoxLogger.LogVerbose(DateTime.Now.ToShortTimeString() + " Overrun, read pointer bumped forward");
                    VivoxLogger.LogVerbose($"Overrun detected with writePointer {m_writePointer} and readPointer {readPointerBef}, setting read pointer to {newPointer}");
                }

                if (m_cachedAudioConfig.dspBufferSize != 0)
                {
                    result = m_owner.DoAudioFilterRead(m_ownerTapId, m_internalBuffer, m_internalAudioPeriodFrames, m_owner.ChannelCount, m_cachedAudioConfig.sampleRate);
                }
                else
                {
                    VivoxLogger.LogVerbose($"WARNING: Cached audio config for {m_owner.Identifier} is in a bad state, falling back to assumptions.");
                    result = m_owner.DoAudioFilterRead(m_ownerTapId, m_internalBuffer, m_internalBuffer.Length / m_owner.ChannelCount, m_owner.ChannelCount, m_assumedVivoxSampleRate);
                }

                if (result != VxErrorNoMoreData)
                {
                    if (result != 0)
                    {
                        VivoxLogger.LogVerbose("Strange buffer pulled");
                    }
                    if (m_missedReads >= m_missedReadsLimit && m_BufferedAudio > m_cachedAudioConfig.dspBufferSize * 2)
                    {
                        //VivoxLogger.LogVerbose($"Unpausing with {m_BufferedAudio} buffered audio");
                        m_audioSource.UnPause();
                        m_missedReads = 0;
                    }
                    else if (m_audioSource.isPlaying)
                    {
                        m_missedReads = 0;
                        m_BufferedAudio = 0;
                    }
                    else
                    {
                        m_BufferedAudio += m_internalAudioPeriodFrames;
                    }
                    m_streamClip.SetData(m_internalBuffer, m_writePointer);
                    // Silence audio that is 80 ms to 100 ms in the future because if audio stops coming in, then this area is at risk of being played by the Audio Source and needs to be silence.
                    //m_streamClip.SetData(m_silenceBufferForAudioPeriod, (m_writePointer + 4 * m_internalAudioPeriodFrames) % m_streamClipTotalFramesSize);

                    // Being overly cautious by silencing audio that is 20 ms to 100 ms in the future.
                    SilenceAudioClipData(m_writePointer + m_internalAudioPeriodFrames, m_writePointer + 5 * m_internalAudioPeriodFrames);

                    m_writePointer = (m_writePointer + m_internalAudioPeriodFrames) % m_streamClipTotalFramesSize;

#if UNITY_EDITOR
                    maxDataOut = GetMax(m_internalBuffer);
#endif // UNITY_EDITOR
                }
                else
                {
                    if (m_missedReads < m_missedReadsLimit * 10)
                    {
                        m_missedReads++;
                    }
                    if (m_missedReads >= m_missedReadsLimit && m_audioSource.isPlaying)
                    {
                        m_audioSource.Pause();
                        SilenceAudioClipData(0, m_streamClipTotalFramesSize);
                    }
                }
            }

            if (result != 0 && result != VxErrorNoMoreData)
            {
                VivoxLogger.LogError($"{m_owner.Identifier} DoAudioFilter method returned error: {result}");
            }

            m_lastReadPointer = m_readPointer;
            m_lastDspTime = AudioSettings.dspTime;

            return maxDataOut;
        }

        // We use a custom function to get the maximum because the Linq Max() method allocates memory,
        // which we want to minimize on the audio thread
        private float GetMax(float[] data)
        {
            var max = -1.0f; // -1.0f should be the lowest value possible in the audio range

            foreach (var value in data)
            {
                if (value > max)
                    max = value;
            }

            return max;
        }

        private void SilenceAudioClipData(int fromPointer, int toPointer)
        {
            if (m_streamClip == null || m_silenceBuffer == null)
            {
                VivoxLogger.LogVerbose("Cannot silence audio clip data, resource is null");
                return;
            }

            // Distance between pointers must be a multiple of dspBufferSize in order to use the cached buffer of silence.
            int silencePeriods;
            if (toPointer >= fromPointer)
            {
                silencePeriods = (toPointer - fromPointer) / (m_cachedAudioConfig.dspBufferSize);
            }
            else
            {
                silencePeriods = (m_streamClipTotalFramesSize - fromPointer + toPointer) / (m_cachedAudioConfig.dspBufferSize);
            }

            for (int periodIndex = 0; periodIndex < silencePeriods; periodIndex++)
            {
                m_streamClip.SetData(m_silenceBuffer, (fromPointer + periodIndex * (m_cachedAudioConfig.dspBufferSize)) % m_streamClipTotalFramesSize);
            }
        }
    }
}
