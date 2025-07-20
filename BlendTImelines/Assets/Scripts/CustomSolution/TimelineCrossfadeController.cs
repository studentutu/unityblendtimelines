using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Timeline;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
//  Drop this on the character that already has an Animator component.
//  ‣ Call Play(timeline, optionalFadeIn, optionalFadeOut) to cross‑fade in a
//    Timeline. Any Timelines already running blend out automatically.
//  ‣ Locomotion (or whatever your Animator Controller does) keeps running
//    underneath because it is pinned in the mixer at weight = 1.
/// </summary>
public class TimelineCrossfadeController : MonoBehaviour
{
    [Tooltip("Default fade‑in time in seconds when Play() is called without an explicit duration.")] [Min(0f)]
    public float defaultFadeIn = 0.25f;

    [Tooltip("Default fade‑out time in seconds used on layers we’re fading *out* automatically.")] [Min(0f)]
    public float defaultFadeOut = 0.3f;

    public Animator Animator;
    public GameObject TimelinesRoot;

    public float MinLocomotionWeightWhenAllGoeDown = 0.35f;

    private sealed class Layer
    {
        public float FadeOutTime;
        public TimelineAsset Asset;
        public PlayableDirector Director;
        public TimelineBlender Blender;
        public Coroutine BlendRoutine;

        public float OldWeightValue;
    }

    private sealed class LocomotionLayer
    {
        public TimelineBlender Blender;
        public Coroutine BlendRoutine;
    }

    private readonly List<Layer> _layers = new();
    private LocomotionLayer _locomotion;

    private void Awake()
    {
        Animator.enabled = true;
    }

    private void OnEnable()
    {
        _locomotion = new LocomotionLayer()
        {
            Blender = new TimelineBlender(Animator, 1f)
        };

        _locomotion.Blender.SetWeight(1f);
    }

    private void OnDisable()
    {
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            var l = _layers[i];
            SafeDestroyLayer(l, immediate: true);
        }

        _layers.Clear();
    }

    private void LateUpdate()
    {
        if (_locomotion.Blender.GetWeight() <= MinLocomotionWeightWhenAllGoeDown && _locomotion.BlendRoutine == null)
            CheckIfLocomotionIsNeeded();
    }

    private void CheckIfLocomotionIsNeeded()
    {
        // 1.  Accumulate the weight of every still‑alive Timeline layer
        float timelineWeight = 0f;
        bool anyPlaying = false;

        foreach (var l in _layers)
        {
            var isInFadeOut = l.Director.time >= l.Asset.duration ||
                              (l.Asset.duration - l.Director.time) <= l.FadeOutTime;
            timelineWeight += l.Director.state == PlayState.Playing && !isInFadeOut ? l.Blender.GetWeight() : 0;
            anyPlaying |= l.Director.state == PlayState.Playing;
        }

        // 2.  If *anything* is still playing *and* its combined weight
        //     is >ε, keep using the blended weight that was already set.
        if (anyPlaying && timelineWeight > MinLocomotionWeightWhenAllGoeDown) return;

        var minFade = defaultFadeOut;
        foreach (var l in _layers)
        {
            minFade = Mathf.Min(minFade, l.FadeOutTime);
            StartFadeOut(l, l.FadeOutTime);
        }


        // 3.  Otherwise: fade locomotion back to full weight.
        if (_locomotion.BlendRoutine != null)
            StopCoroutine(_locomotion.BlendRoutine);

        _locomotion.BlendRoutine =
            StartCoroutine(BlendWeight(
                _locomotion.Blender,
                1f,
                minFade,
                () => _locomotion.BlendRoutine = null));
    }

    /// <summary>
    /// Cross‑fades <paramref name="timeline"/> on top of the Animator Controller.
    /// </summary>
    public void Play(TimelineAsset timeline, float maxWeight = 1f, float fadeIn = 1, float fadeOut = 1)
    {
        if (!timeline)
        {
            Debug.LogWarning("Timeline asset is null.", this);
            return;
        }

        fadeIn = fadeIn < 0 ? defaultFadeIn : fadeIn;
        fadeOut = fadeOut < 0 ? defaultFadeOut : fadeOut;

        // Restart if we’re already running that asset.
        var existing = _layers.Find(l => l.Asset == timeline);
        if (existing != null)
        {
            RestartLayer(existing, maxWeight, fadeIn);
            return;
        }

        var dirGO = new GameObject($"Timeline_{timeline.name}");
        dirGO.transform.SetParent(TimelinesRoot.transform);

        // TODO: Best to use pre-setup playable director (in order to get all of the references for all track objects)
        var director = dirGO.AddComponent<PlayableDirector>();
        director.playOnAwake = false;
        director.timeUpdateMode = DirectorUpdateMode.GameTime;
        director.extrapolationMode = DirectorWrapMode.None;
        director.playableAsset = timeline;

        BindTargetsToDirecterForTimeline(timeline, director);
        director.RebuildGraph(); // makes outputs immediately

        var timelineBlender = new TimelineBlender(director, 0);

        var layer = new Layer
        {
            Asset = timeline,
            Director = director,
            Blender = timelineBlender,
            FadeOutTime = fadeOut,
        };
        timelineBlender.SetWeight(0);
        director.Play();

        _layers.Add(layer);
        layer.BlendRoutine =
            StartCoroutine(BlendWeight(layer.Blender, maxWeight, fadeIn, () => layer.BlendRoutine = null));
        _locomotion.BlendRoutine = StartCoroutine(BlendWeight(_locomotion.Blender, 1f - maxWeight, fadeIn * 2,
            () => _locomotion.BlendRoutine = null));

        foreach (var l in _layers)
            if (l != layer)
                StartFadeOut(l, fadeOut);
    }

    // TODO: Not now. Make sure when creating director to update actual references to the objects!
    // Director requires to setup Animator, GameObjects, VfX and Audio
    // Custom timeline playable gameobjects etc.
    // It is easier ot use already pre-setup directors and just enable/disable them.
    private void BindTargetsToDirecterForTimeline(TimelineAsset timeline, PlayableDirector director)
    {
        foreach (var output in timeline.outputs)
        {
            if (typeof(Animator).IsAssignableFrom(output.outputTargetType))
                director.SetGenericBinding(output.sourceObject, Animator);
        }
    }

    /// <summary>Cuts every Timeline immediately (Animator Controller keeps running).</summary>
    public void StopAllTimelines()
    {
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            var l = _layers[i];
            if (l.BlendRoutine != null) StopCoroutine(l.BlendRoutine);
            l.Blender.SetWeight(0f);
            SafeDestroyLayer(l, immediate: true);
        }

        _layers.Clear();
        if (_locomotion.BlendRoutine != null) StopCoroutine(_locomotion.BlendRoutine);

        _locomotion.BlendRoutine = null;
        _locomotion.Blender.SetWeight(1f);
    }

    // ───────────────────────────────────── INTERNAL HELPERS ────────────────────────────────────
    private void RestartLayer(Layer layer, float maxWeight, float fadeIn)
    {
        layer.Director.time = 0;
        layer.Director.Play();

        if (layer.BlendRoutine != null) StopCoroutine(layer.BlendRoutine);
        layer.BlendRoutine =
            StartCoroutine(BlendWeight(layer.Blender, maxWeight, fadeIn, () => layer.BlendRoutine = null));

        if (_locomotion.BlendRoutine != null) StopCoroutine(_locomotion.BlendRoutine);
        _locomotion.BlendRoutine = StartCoroutine(BlendWeight(_locomotion.Blender, 1f - maxWeight, fadeIn,
            () => _locomotion.BlendRoutine = null));
    }

    private void StartFadeOut(Layer layer, float duration)
    {
        if (layer.BlendRoutine != null) StopCoroutine(layer.BlendRoutine);

        layer.BlendRoutine = StartCoroutine(BlendWeight(layer.Blender, 0f, duration,
            onDone: () => SafeDestroyLayer(layer, immediate: false)));
    }

    private IEnumerator BlendWeight(TimelineBlender layer, float target, float duration, Action onDone = null)
    {
        float start = layer.GetWeight();
        float clock = 0f;
        while (clock < duration)
        {
            clock += Time.deltaTime;
            float t = duration > 0 ? clock / duration : 1;
            layer.SetWeight(Mathf.Lerp(start, target, t));
            yield return null;
        }

        layer.SetWeight(target);
        onDone?.Invoke();
    }

    private void SafeDestroyLayer(Layer l, bool immediate)
    {
        l.Director.Stop();
        if (immediate) DestroyImmediate(l.Director.gameObject);
        else Destroy(l.Director.gameObject);
        _layers.Remove(l);
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(TimelineCrossfadeController))]
    private class TimelineCrossfadeControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Stop All Timelines"))
                ((TimelineCrossfadeController)target).StopAllTimelines();
        }
    }
#endif
}

/// <summary>
/// This behaviour is used to set the weight of the animation output.
/// </summary>
public class SetAnimationOutputWeightBehaviour : PlayableBehaviour
{
    public float weight;
    public bool OutputsAreUpdated;
    public float ActualWeight { get; private set; }
    public List<AnimationPlayableOutput> outputList = new();

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        foreach (var output in outputList)
        {
            var getRaw = output.GetWeight();
            if (!OutputsAreUpdated)
                getRaw = info.weight;

            ActualWeight = weight * getRaw;
            output.SetWeight(ActualWeight);
        }
    }
}

/// <summary>
/// Provides the ability to fade a graph animation output weight over time.
/// <para>Each <see cref="TimelineBlender"/> instance handles one graph.</para>
/// </summary>
public class TimelineBlender
{
    /// <summary> weight of animation outputs (set every frame) </summary>
    private float _weight = 0f;

    private SetAnimationOutputWeightBehaviour behaviour;
    private string Name;

    public TimelineBlender(PlayableDirector director, float initialWeight)
    {
        _weight = initialWeight;
        Name = director.playableAsset.name;

        if (!director) return;

        // register events
        director.played += CreateSetWeightBehaviour;
        director.stopped += OnPlayableDirectorStopped;

        // If the director is already playing, manually create the behaviour.
        // (playOnAwake triggered Playing but won't call director.played event)
        if (director.playOnAwake && director.playableGraph.IsValid())
        {
            CreateSetWeightBehaviour(director);
        }
    }

    public TimelineBlender(Animator animator, float initialWeight)
    {
        _weight = initialWeight;
        Name = animator.runtimeAnimatorController.name;

        if (!animator) return;

        if (animator.playableGraph.IsValid())
        {
            CreateSetWeightBehaviour(animator.playableGraph);
        }
    }

    private void CreateSetWeightBehaviour(PlayableDirector director)
    {
        CreateSetWeightBehaviour(director.playableGraph);
        behaviour.OutputsAreUpdated = true;
    }

    private void CreateSetWeightBehaviour(PlayableGraph graph)
    {
        if (!graph.IsValid()) return;

        var playable = ScriptPlayable<SetAnimationOutputWeightBehaviour>.Create(graph);
        var output = ScriptPlayableOutput.Create(graph, "SetAnimationOutputWeight");
        output.SetSourcePlayable(playable);
        behaviour = playable.GetBehaviour();

        // initialize behaviour
        behaviour.weight = _weight;
        var outputCount = graph.GetOutputCount();

        // Behaviour which overwrites outputs
        // add all outputs to behaviour
        // you can extend this to only add the outputs you want to control
        for (var i = 0; i < outputCount; i++)
        {
            var outputAt = graph.GetOutput(i);

            if (outputAt.IsPlayableOutputOfType<AnimationPlayableOutput>())
                behaviour.outputList.Add((AnimationPlayableOutput)graph.GetOutput(i));
        }
    }

    private void OnPlayableDirectorStopped(PlayableDirector obj)
    {
        behaviour = null;
    }


    public float GetWeight()
    {
        if (behaviour == null) return 0;
        return behaviour.ActualWeight;
    }

    public void SetWeight(float weight)
    {
        _weight = weight;
        if (behaviour == null) return;
        behaviour.weight = weight;
    }
}