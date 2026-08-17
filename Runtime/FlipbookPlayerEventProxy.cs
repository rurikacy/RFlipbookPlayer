using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(FlipbookPlayer))]
[AddComponentMenu("Custom/Flipbook 代理帧事件器")]
public class FlipbookPlayerEventProxy : MonoBehaviour
{
    [SerializeField] private FlipbookPlayer player;
    [SerializeField] private List<FrameEvent> frameEvents = new();
    [SerializeField] private UnityEvent onCompleted;

    private readonly HashSet<int> _triggeredFrameNumbers = new();
    private bool _completedThisPlay;
    private int _observedLoopCount;
    private int _observedPlaybackSequenceId;
    private int _previousFrameNumber;

    public UnityEvent OnCompleted => onCompleted;
    public int CurrentFrameNumber => player ? player.CurrentFrameNumber : 0;

    private void Awake()
    {
        if (!player) player = GetComponent<FlipbookPlayer>();
    }

    private void Reset()
    {
        player = GetComponent<FlipbookPlayer>();
    }

    private void Update()
    {
        if (!player) return;

        if (_observedPlaybackSequenceId != player.PlaybackSequenceId)
        {
            BeginTracking();
            _observedPlaybackSequenceId = player.PlaybackSequenceId;
        }

        if (!player.IsPlaying)
            return;

        int loopCount = player.PlaybackLoopCount;
        if (loopCount != _observedLoopCount)
        {
            int completedLoops = loopCount - _observedLoopCount;
            int totalFrames = player.GetTotalFrames();

            if (completedLoops > 0)
            {
                DispatchFrameEvents(_previousFrameNumber, totalFrames);

                for (int i = 1; i < completedLoops; i++)
                {
                    _triggeredFrameNumbers.Clear();
                    DispatchFrameEvents(0, totalFrames);
                }
            }

            _triggeredFrameNumbers.Clear();
            _previousFrameNumber = 0;
            _observedLoopCount = loopCount;
        }

        int currentFrameNumber = player.CurrentFrameNumber;
        DispatchFrameEvents(_previousFrameNumber, currentFrameNumber);
        _previousFrameNumber = currentFrameNumber;
    }

    private void OnEnable()
    {
        if (!player) player = GetComponent<FlipbookPlayer>();

        BeginTracking();
        _observedPlaybackSequenceId = player ? player.PlaybackSequenceId : 0;
        if (player) player.PlaybackCompleted += OnPlayerPlaybackCompleted;
    }

    private void OnDisable()
    {
        if (player) player.PlaybackCompleted -= OnPlayerPlaybackCompleted;
    }

    /// <summary>
    ///     C# 事件，播放完成时触发。支持 += / -= 订阅。
    /// </summary>
    public event Action Completed;

    /// <summary>
    ///     C# 事件，到达任意帧事件时触发，参数为帧号。支持 += / -= 订阅。
    /// </summary>
    public event Action<int> FrameReached;

    public void Play()
    {
        if (!player) player = GetComponent<FlipbookPlayer>();

        player.Play();
        BeginTracking();
        _observedPlaybackSequenceId = player.PlaybackSequenceId;
    }

    public void Pause()
    {
        player?.Pause();
    }

    public void Resume()
    {
        player?.Resume();
    }

    public void Stop()
    {
        player?.Stop();
        BeginTracking();
        if (player) _observedPlaybackSequenceId = player.PlaybackSequenceId;
    }

    private void BeginTracking()
    {
        _observedLoopCount = player ? player.PlaybackLoopCount : 0;
        _previousFrameNumber = 0;
        _completedThisPlay = false;
        _triggeredFrameNumbers.Clear();
    }

    private void OnPlayerPlaybackCompleted(FlipbookPlayer completedPlayer)
    {
        if (completedPlayer != player) return;

        if (_observedPlaybackSequenceId != player.PlaybackSequenceId)
        {
            BeginTracking();
            _observedPlaybackSequenceId = player.PlaybackSequenceId;
        }

        if (_completedThisPlay) return;

        int totalFrames = player.GetTotalFrames();
        if (totalFrames > 0)
        {
            DispatchFrameEvents(_previousFrameNumber, totalFrames);
            _previousFrameNumber = totalFrames;
        }

        _completedThisPlay = true;
        onCompleted?.Invoke();
        Completed?.Invoke();
    }

    private void DispatchFrameEvents(int previousFrameNumber, int currentFrameNumber)
    {
        if (currentFrameNumber <= previousFrameNumber) return;

        for (int i = 0; i < frameEvents.Count; i++)
        {
            FrameEvent frameEvent = frameEvents[i];
            if (frameEvent == null) continue;

            int frameNumber = frameEvent.frameNumber;
            if (frameNumber <= previousFrameNumber || frameNumber > currentFrameNumber) continue;

            if (_triggeredFrameNumbers.Add(frameNumber))
            {
                frameEvent.onReached?.Invoke();
                FrameReached?.Invoke(frameNumber);
            }
        }
    }

    [Serializable]
    public class FrameEvent
    {
        [Min(1)]
        public int frameNumber = 1;
        public UnityEvent onReached;
    }
}