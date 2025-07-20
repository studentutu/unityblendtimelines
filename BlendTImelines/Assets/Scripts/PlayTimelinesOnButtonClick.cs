using UnityEngine;
using UnityEngine.Timeline;

public class PlayTimelinesOnButtonClick : MonoBehaviour
{
    public TimelineCrossfadeController TimelineCrossfader;
   
    void OnEnable()
    {
        ButtonClickedSource.ButtonClickedEvent += ButtonClicked_ButtonClickedEvent;
    }
    void OnDisable()
    {
        ButtonClickedSource.ButtonClickedEvent -= ButtonClicked_ButtonClickedEvent;
    }
    private void ButtonClicked_ButtonClickedEvent(TimelineAsset timeline)
    {
        TimelineCrossfader.Play(timeline);
    }
}
