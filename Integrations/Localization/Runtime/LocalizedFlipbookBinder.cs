using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

/// <summary>
///     监听 Unity Localization 资源变化，并将当前语言对应的 FlipbookClip 应用到播放器。
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(FlipbookPlayer))]
[AddComponentMenu("Custom/Flipbook 本地化绑定器")]
public class LocalizedFlipbookBinder : MonoBehaviour
{
    [SerializeField] private FlipbookPlayer player;
    [SerializeField] private LocalizedFlipbookClip localizedClip = new();

    private LocalizedAsset<FlipbookClip>.ChangeHandler _clipChanged;
    private LocalizedFlipbookClip _subscribedClip;

    /// <summary>获取当前绑定的播放器。</summary>
    public FlipbookPlayer Player => player;

    /// <summary>获取当前本地化 Flipbook 资源引用。</summary>
    public LocalizedFlipbookClip LocalizedClip => localizedClip;

    private void Awake()
    {
        ResolvePlayer();
    }

    private void Reset()
    {
        ResolvePlayer();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;

        ResolvePlayer();
        EnsureLocalizedClip();

        _clipChanged ??= ApplyLocalizedClip;
        _subscribedClip = localizedClip;
        _subscribedClip.AssetChanged += _clipChanged;
    }

    private void OnDisable()
    {
        if (_subscribedClip != null && _clipChanged != null)
            _subscribedClip.AssetChanged -= _clipChanged;
        _subscribedClip = null;
    }

    private void OnValidate()
    {
        ResolvePlayer();
        EnsureLocalizedClip();
    }

    /// <summary>立即将指定 Clip 应用到播放器。</summary>
    /// <param name="clip">要应用的 Flipbook 配置。</param>
    public void ApplyClip(FlipbookClip clip)
    {
        ApplyLocalizedClip(clip);
    }

    private void ApplyLocalizedClip(FlipbookClip clip)
    {
        ResolvePlayer();
        if (!player) return;

        if (!clip)
        {
            if (localizedClip is { IsEmpty: false })
                Debug.LogWarning($"Localized flipbook clip is missing for {localizedClip}.", this);

            return;
        }

        bool wasPlaying = Application.isPlaying && player.IsPlaying;

        ApplyClipData(clip);
        player.CalculateSegmentTime();

        if (wasPlaying)
            player.Play();
        else
            player.RefreshPreview();
    }

    private void ApplyClipData(FlipbookClip clip)
    {
        player.row = Mathf.Max(1, clip.row);
        player.column = Mathf.Max(1, clip.column);
        player.frameRate = Mathf.Max(1, clip.frameRate);
        player.frameSourceMode = clip.frameSourceMode;

        player.textureList ??= new List<Texture2D>();
        player.frameList ??= new List<int>();
        player.multipleFrameUvList ??= new List<Rect>();

        player.textureList.Clear();
        player.frameList.Clear();
        player.multipleFrameUvList.Clear();

        if (clip.textureList == null) return;

        for (int i = 0; i < clip.textureList.Count; i++)
        {
            player.textureList.Add(clip.textureList[i]);
            player.frameList.Add(clip.GetSafeFrameCount(i));
        }

        if (clip.multipleFrameUvList != null)
            player.multipleFrameUvList.AddRange(clip.multipleFrameUvList);
    }

    private void ResolvePlayer()
    {
        if (!player) player = GetComponent<FlipbookPlayer>();
    }

    private void EnsureLocalizedClip()
    {
        localizedClip ??= new LocalizedFlipbookClip();
    }
}
