using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

public class ButtonClickedSource : MonoBehaviour
{
    public TimelineAsset Timeline;

    public static event System.Action<TimelineAsset> ButtonClickedEvent;    
    public void OnClick()
    {
        ButtonClickedEvent?.Invoke(Timeline);
    }
}
