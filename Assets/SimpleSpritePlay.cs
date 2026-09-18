using UnityEngine;
using UnityEngine.UI;

public class SimpleSpritePlay : MonoBehaviour
{
    [Header("序列帧（按顺序播放）")]
    public Sprite[] spriteFrames;

    [Header("播放设置")]
    public float fps = 30f;
    public bool isLoop = false;

    private SpriteRenderer _spr;
    private float _timer = 0;
    private int _curIndex = 0;
    private bool _isEnd = false;

    private void Awake()
    {
        _spr = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        if (_isEnd == true) return;
        if (spriteFrames == null || spriteFrames.Length == 0) return;

        _timer += Time.deltaTime;
        float interval = fps <= 0 ? float.MaxValue : 1f / fps;
        if (_timer >= interval)
        {
            _timer = 0f;
            NextFrame();
        }
    }

    void NextFrame()
    {
        if (_curIndex >= spriteFrames.Length)
        {
            if (isLoop)
                _curIndex = 0;
            else
            {
                _spr.sprite = null;
                _isEnd = true;
                return;
            }
        }

        _spr.sprite = spriteFrames[_curIndex];
        _curIndex++;
    }

    private void OnEnable() 
    { 
        _curIndex = 0; 
        _timer = 0;
        _isEnd = false;

        if (spriteFrames != null && spriteFrames.Length > 0)
        {
            _spr.sprite = spriteFrames[0];
            _curIndex = 1; 
        }
    }
    private void OnDisable() 
    { 
        _curIndex = 0;
        _isEnd = true;
    }
}
