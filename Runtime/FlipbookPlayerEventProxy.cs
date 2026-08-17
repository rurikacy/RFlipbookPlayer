using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
///     将 Flipbook 帧到达与播放完成通知桥接为 UnityEvent 和 C# 事件。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FlipbookPlayer))]
[AddComponentMenu("Custom/Flipbook 代理帧事件器")]
public class FlipbookPlayerEventProxy : MonoBehaviour
{
    [SerializeField] private FlipbookPlayer player;
    [SerializeField] private List<FrameEvent> frameEvents = new();
    [SerializeField] private UnityEvent onCompleted = new();

    private bool _completedThisPlay;
    private int _observedLoopCount;
    private int _observedPlaybackSequenceId;
    private int _previousFrameNumber;

    /// <summary>获取 Inspector 中配置的播放完成事件。</summary>
    public UnityEvent OnCompleted => onCompleted;

    /// <summary>获取播放器当前的全局帧号。</summary>
    public int CurrentFrameNumber => player ? player.CurrentFrameNumber : 0;

    /// <summary>播放完成时触发。</summary>
    public event Action Completed;

    /// <summary>到达已配置的任意帧时触发，参数为从 1 开始的全局帧号。</summary>
    public event Action<int> FrameReached;

    private void Awake()
    {
        if (!player) player = GetComponent<FlipbookPlayer>();
    }

    private void Reset()
    {
        player = GetComponent<FlipbookPlayer>();
    }

    private void OnValidate()
    {
        if (!player) player = GetComponent<FlipbookPlayer>();
        frameEvents ??= new List<FrameEvent>();
        onCompleted ??= new UnityEvent();
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
                    DispatchFrameEvents(0, totalFrames);
                }
            }

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

    /// <summary>从第一帧重新开始播放。</summary>
    public void Play()
    {
        if (!player) player = GetComponent<FlipbookPlayer>();
        if (!player) return;

        player.Play();
        BeginTracking();
        _observedPlaybackSequenceId = player.PlaybackSequenceId;
    }

    /// <summary>暂停播放并保留当前位置。</summary>
    public void Pause()
    {
        player?.Pause();
    }

    /// <summary>从当前位置恢复播放。</summary>
    public void Resume()
    {
        player?.Resume();
    }

    /// <summary>停止播放并回到第一帧。</summary>
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

        if (frameEvents == null) return;

        for (int i = 0; i < frameEvents.Count; i++)
        {
            FrameEvent frameEvent = frameEvents[i];
            if (frameEvent == null) continue;

            int frameNumber = frameEvent.frameNumber;
            if (frameNumber <= previousFrameNumber || frameNumber > currentFrameNumber) continue;

            frameEvent.onReached?.Invoke();
            FrameReached?.Invoke(frameNumber);
        }
    }

    /// <summary>描述一个从 1 开始的全局帧事件。</summary>
    [Serializable]
    public class FrameEvent
    {
        /// <summary>从 1 开始的全局帧号。</summary>
        [Min(1)]
        public int frameNumber = 1;

        /// <summary>到达指定帧时调用的 UnityEvent。</summary>
        public UnityEvent onReached = new();
    }
}
