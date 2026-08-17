using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(FlipbookPlayer))]
[AddComponentMenu("Custom/Flipbook 本地化绑定器")]
public class LocalizedFlipbookBinder : MonoBehaviour
{
    [SerializeField] private FlipbookPlayer player;
    [SerializeField] private LocalizedFlipbookClip localizedClip = new();

    private LocalizedAsset<FlipbookClip>.ChangeHandler _clipChanged;

    public FlipbookPlayer Player => player;
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
        localizedClip.AssetChanged += _clipChanged;
    }

    private void OnDisable()
    {
        if (localizedClip != null && _clipChanged != null)
            localizedClip.AssetChanged -= _clipChanged;
    }

    private void OnValidate()
    {
        ResolvePlayer();
        EnsureLocalizedClip();
    }

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