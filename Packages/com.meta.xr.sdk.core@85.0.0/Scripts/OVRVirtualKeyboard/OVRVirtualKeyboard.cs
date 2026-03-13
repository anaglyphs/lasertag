/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;
using System.Linq;
using Meta.XR.Util;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

/// <summary>
/// Enables Virtual Keyboard integration.
/// </summary>
[DisallowMultipleComponent]
[HelpURL("https://developer.oculus.com/documentation/unity/VK-unity-IntegratePrefab/")]

[Feature(Feature.VirtualKeyboard)]
public class OVRVirtualKeyboard : MonoBehaviour
{

    private static readonly string _defaultShaderName = "Unlit/Color";
    private static readonly string _defaultAlphaBlendShaderName = "Unlit/Transparent";
    /// <summary>
    /// The initial position of the keyboard, which determines the input style used to type. Far uses raycasting to type. Near uses direct touch to type. If set to Far or Near, the keyboard position is runtime controlled, so the Transform component will be locked.
    /// </summary>
    public enum KeyboardPosition
    {
        Far = 0,
        Near = 1,
        [Obsolete]
        Direct = 1,
        Custom = 2,
    }

    public interface ITextHandler
    {
        public Action<string> OnTextChanged { set; get; }

        public string Text { get; }
        public bool SubmitOnEnter { get; }
        public bool IsFocused { get; }

        public void Submit();
        public void AppendText(string s);
        public void ApplyBackspace();
        public void MoveTextEnd();
    }

    public abstract class AbstractTextHandler : MonoBehaviour, ITextHandler
    {
        public abstract Action<string> OnTextChanged { get; set; }

        public abstract string Text { get; }
        public abstract bool SubmitOnEnter { get; }
        public abstract bool IsFocused { get; }

        public abstract void Submit();
        public abstract void AppendText(string s);
        public abstract void ApplyBackspace();
        public abstract void MoveTextEnd();
    }

    private class TextHandlerScope : ITextHandler, IDisposable
    {
        private readonly ITextHandler _textHandler;
        private readonly Action<string> _textChangeHandler;
        public TextHandlerScope(ITextHandler textHandler, Action<string> textChangeHandler)
        {
            _textHandler = textHandler;
            _textChangeHandler = textChangeHandler;

            _textHandler.OnTextChanged -= _textChangeHandler;
        }

        public void Dispose()
        {
            _textHandler.OnTextChanged += _textChangeHandler;
        }

        public Action<string> OnTextChanged { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string Text => _textHandler.Text;
        public bool SubmitOnEnter => _textHandler.SubmitOnEnter;
        public bool IsFocused => _textHandler.IsFocused;
        public void Submit()
        {
            _textHandler?.Submit();
        }

        public void AppendText(string s)
        {
            if (_textHandler == null)
            {
                return;
            }
            _textHandler.AppendText(s);
            // Text Context currently expects an end of text caretPosition
            if (_textHandler.IsFocused)
            {
                _textHandler.MoveTextEnd();
            }
        }

        public void ApplyBackspace()
        {
            if (_textHandler == null)
            {
                return;
            }
            _textHandler.ApplyBackspace();
            // Text Context currently expects an end of text caretPosition
            if (_textHandler.IsFocused)
            {
                _textHandler.MoveTextEnd();
            }
        }

        public void MoveTextEnd()
        {
            _textHandler?.MoveTextEnd();
        }
    }

    /// <summary>
    /// Keyboard initialization is async so this yield instruction is available for use in coroutines that an action
    /// performed once the keyboard is visible
    /// </summary>
    public class WaitUntilKeyboardVisible : CustomYieldInstruction
    {
        private readonly OVRVirtualKeyboard _keyboard;
        public override bool keepWaiting => !_keyboard.modelAvailable_ || !_keyboard.keyboardVisible_;

        public WaitUntilKeyboardVisible(OVRVirtualKeyboard keyboard)
        {
            _keyboard = keyboard;
        }
    }

    public class InteractorRootTransformOverride
    {
        private struct InteractorRootOverrideData
        {
            public Transform root;
            public OVRPose originalPose;
            public OVRPose targetPose;
        }

        private Queue<InteractorRootOverrideData> applyQueue = new Queue<InteractorRootOverrideData>();
        private Queue<InteractorRootOverrideData> revertQueue = new Queue<InteractorRootOverrideData>();

        public void Enqueue(Transform interactorRootTransform, OVRPlugin.Posef interactorRootPose)
        {
            if (interactorRootTransform == null)
            {
                throw new Exception("Transform is undefined");
            }

            applyQueue.Enqueue(new InteractorRootOverrideData()
            {
                root = interactorRootTransform,
                originalPose = interactorRootTransform.ToOVRPose(),
                targetPose = interactorRootPose.ToOVRPose()
            });
        }

        public void LateApply(MonoBehaviour coroutineRunner)
        {
            while (applyQueue.Count > 0)
            {
                var queueItem = applyQueue.Dequeue();
                var restoreToPose = queueItem.root.ToOVRPose();
                if (!ApplyOverride(queueItem))
                {
                    continue;
                }

                queueItem.originalPose = queueItem.root.ToOVRPose();
                queueItem.targetPose = restoreToPose;
                revertQueue.Enqueue(queueItem);
            }

            if (revertQueue.Count > 0 && coroutineRunner != null)
            {
                coroutineRunner.StartCoroutine(RevertInteractorOverrides());
            }
        }

        public void Reset()
        {
            while (revertQueue.Count > 0)
            {
                ApplyOverride(revertQueue.Dequeue());
            }
        }

        private IEnumerator RevertInteractorOverrides()
        {
            yield return new WaitForEndOfFrame();
            Reset();
        }

        private static bool ApplyOverride(InteractorRootOverrideData interactorOverride)
        {
            if (interactorOverride.root.position != interactorOverride.originalPose.position ||
                interactorOverride.root.rotation != interactorOverride.originalPose.orientation)
            {
                return false;
            }

            interactorOverride.root.position = interactorOverride.targetPose.position;
            interactorOverride.root.rotation = interactorOverride.targetPose.orientation;
            return true;
        }
    }

    public enum InputSource
    {
        ControllerLeft,
        ControllerRight,
        HandLeft,
        HandRight
    }

    private interface IInputSource : IDisposable
    {
        void Update();
    }

    private abstract class BaseInputSource : IInputSource
    {
        protected readonly bool _operatingWithoutOVRCameraRig;
        private readonly OVRCameraRig _rig;
        private bool _disposed = false;

        protected BaseInputSource()
        {
            _rig = FindAnyObjectByType<OVRCameraRig>();
            if (_rig == null) return;
            _rig.UpdatedAnchors += OnUpdatedAnchors;
            _operatingWithoutOVRCameraRig = false;
        }

        private void OnUpdatedAnchors(OVRCameraRig obj)
        {
            if (_disposed)
            {
                // if the ovrCameraRig reference is in an unexpected state
                throw new Exception("Virtual Keyboard Input Source Disposed");
            }
            UpdateInput();
        }

        public void Update()
        {
            if (_operatingWithoutOVRCameraRig && !_disposed)
            {
                UpdateInput();
            }
        }

        protected abstract void UpdateInput();

        public void Dispose()
        {
            _disposed = true;
            if (_rig != null)
            {
                _rig.UpdatedAnchors -= OnUpdatedAnchors;
            }
        }
    }
    private class ControllerInputSource : BaseInputSource
    {
        private readonly Transform _rootTransform;
        private readonly Transform _directTransform;
        private readonly InputSource _inputSource;
        private readonly OVRInput.Controller _controllerType;
        private readonly OVRVirtualKeyboard _keyboard;
        private int _lastFrameCount;

        private bool TriggerIsPressed => OVRInput.Get(
            _controllerType == OVRInput.Controller.LTouch
            ? OVRInput.RawButton.LIndexTrigger | OVRInput.RawButton.X
            : OVRInput.RawButton.RIndexTrigger | OVRInput.RawButton.A);

        public ControllerInputSource(OVRVirtualKeyboard keyboard, InputSource inputSource,
            OVRInput.Controller controllerType, Transform rootTransform, Transform directTransform) : base()
        {
            _keyboard = keyboard;
            _inputSource = inputSource;
            _controllerType = controllerType;
            _rootTransform = rootTransform;
            _directTransform = directTransform;
        }

        protected override void UpdateInput()
        {
            if (!_keyboard.InputEnabled || !OVRInput.GetControllerPositionValid(_controllerType) || !_rootTransform)
            {
                return;
            }

            if (Time.frameCount == _lastFrameCount)
            {
                // Input already sent for frame
                return;
            }
            _lastFrameCount = Time.frameCount;

            if (_keyboard.controllerRayInteraction)
            {
                _keyboard.SendVirtualKeyboardRayInput(
                    _directTransform, _inputSource,
                    TriggerIsPressed);
            }

            if (_keyboard.controllerDirectInteraction)
            {
                _keyboard.SendVirtualKeyboardDirectInput(
                    _directTransform.position,
                    _inputSource,
                    TriggerIsPressed,
                    _rootTransform);
            }
        }
    }

    private class HandInputSource : BaseInputSource
    {
        private readonly OVRHand _hand;
        private readonly InputSource _inputSource;
        private readonly OVRVirtualKeyboard _keyboard;
        private readonly OVRSkeleton _skeleton;
        private int _lastFrameCount;

        public HandInputSource(OVRVirtualKeyboard keyboard, InputSource inputSource, OVRHand hand) : base()
        {
            if (!keyboard)
            {
                throw new ArgumentNullException("keyboard");
            }
            _keyboard = keyboard;
            if (!hand)
            {
                throw new ArgumentNullException("hand");
            }
            _hand = hand;
            _skeleton = _hand.GetComponent<OVRSkeleton>();
            if (!_skeleton && _keyboard.handDirectInteraction)
            {
                Debug.LogWarning("Hand Direct Interaction requires an OVRSkeleton on the OVRHand");
            }
            _inputSource = inputSource;
        }

        protected override void UpdateInput()
        {
            if (!_keyboard.InputEnabled || !_hand)
            {
                return;
            }

            if (Time.frameCount == _lastFrameCount)
            {
                // Input already sent for frame
                return;
            }
            _lastFrameCount = Time.frameCount;

            if (_keyboard.handRayInteraction && _hand.IsPointerPoseValid)
            {
                _keyboard.SendVirtualKeyboardRayInput(
                    _hand.PointerPose,
                    _inputSource, _hand.GetFingerIsPinching(OVRHand.HandFinger.Index));
            }

            if (_keyboard.handDirectInteraction && _skeleton && _skeleton.IsDataValid)
            {
                OVRSkeleton.BoneId indexTipJoint =
                    _skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft ||
                    _skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight ?
                    OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip;

                OVRSkeleton.BoneId wristRootJoint =
                    _skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft ||
                    _skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight ?
                    OVRSkeleton.BoneId.XRHand_Wrist : OVRSkeleton.BoneId.Hand_WristRoot;

                var indexTip = _skeleton.Bones.First(b => b.Id == indexTipJoint);
                var interactorRoot = _skeleton.Bones.First(b => b.Id == wristRootJoint);
                _keyboard.SendVirtualKeyboardDirectInput(
                    indexTip.Transform.position,
                    _inputSource, _hand.GetFingerIsPinching(OVRHand.HandFinger.Index), interactorRoot.Transform);
            }
        }
    }

    private class KeyboardEventListener : OVRManager.EventListener
    {
        private readonly OVRVirtualKeyboard keyboard_;

        public KeyboardEventListener(OVRVirtualKeyboard keyboard)
        {
            this.keyboard_ = keyboard;
        }

        public void OnEvent(OVRPlugin.EventDataBuffer eventDataBuffer)
        {
            switch (eventDataBuffer.EventType)
            {
                case OVRPlugin.EventType.VirtualKeyboardCommitText:
                {
                    if (keyboard_.CommitTextEvent != null || keyboard_.CommitText != null)
                    {
                        var eventData = Encoding.UTF8.GetString(eventDataBuffer.EventData)
                        .Replace("\0", "");
                        keyboard_.CommitTextEvent?.Invoke(eventData);
                        keyboard_.CommitText?.Invoke(eventData);
                    }
                    break;
                }
                case OVRPlugin.EventType.VirtualKeyboardBackspace:
                {
                    keyboard_.BackspaceEvent?.Invoke();
                    keyboard_.Backspace?.Invoke();
                    break;
                }
                case OVRPlugin.EventType.VirtualKeyboardEnter:
                {
                    keyboard_.EnterEvent?.Invoke();
                    keyboard_.Enter?.Invoke();
                    break;
                }
                case OVRPlugin.EventType.VirtualKeyboardShown:
                {
                    keyboard_.KeyboardShownEvent?.Invoke();
                    keyboard_.KeyboardShown?.Invoke();
                    break;
                }
                case OVRPlugin.EventType.VirtualKeyboardHidden:
                {
                    keyboard_.KeyboardHiddenEvent?.Invoke();
                    keyboard_.KeyboardHidden?.Invoke();
                    break;
                }
            }
        }

    }

    private struct VirtualKeyboardTextureInfo
    {
        public IntPtr buffer;
        public uint bufferLength;
        public Texture2D texture;
        public bool hasTexture;
        public List<Material> materials;
    }

    private static OVRVirtualKeyboard singleton_;

    /// <summary>
    /// Occurs when text has been committed
    /// @params (string text)
    /// </summary>
    [Obsolete("Use CommitTextEvent", false)]
    public event Action<string> CommitText;

    /// <summary>
    /// Occurs when a backspace is pressed
    /// </summary>
    [Obsolete("Use BackspaceEvent", false)]
    public event Action Backspace;

    /// <summary>
    /// Occurs when a return key is pressed
    /// </summary>
    [Obsolete("Use EnterEvent", false)]
    public event Action Enter;

    /// <summary>
    /// Occurs when keyboard is shown
    /// </summary>
    [Obsolete("Use KeyboardShownEvent", false)]
    public event Action KeyboardShown;

    /// <summary>
    /// Occurs when keyboard is hidden
    /// </summary>
    [Obsolete("Use KeyboardHiddenEvent", false)]
    public event Action KeyboardHidden;

    public Collider Collider { get; private set; }

    [SerializeField]
    private KeyboardPosition InitialPosition = KeyboardPosition.Custom;

    /// <summary>
    /// Unity UI field to automatically commit text into. (optional)
    /// </summary>
    [SerializeField]
    [FormerlySerializedAs("TextCommitField")]
    [Obsolete]
    [HideInInspector]
    private InputField textCommitField;

    /// <summary>
    /// Use to connect the Virtual Keyboard to various text sources, like a Unity Input Fields via OVRVirtualKeyboardUnityInputFieldTextHandler
    /// </summary>
    [SerializeField]
    private AbstractTextHandler textHandler;
    private ITextHandler _runtimeTextHandler;

    [Header("Controller Input")]
    /// <summary>
    /// Configure with the transform representing the left controller input.
    /// </summary>
    [FormerlySerializedAs("leftControllerInputTransform")]
    public Transform leftControllerRootTransform;
    public Transform leftControllerDirectTransform;

    /// <summary>
    /// Configure with the transform representing the right controller input.
    /// </summary>
    [FormerlySerializedAs("rightControllerInputTransform")]
    public Transform rightControllerRootTransform;
    public Transform rightControllerDirectTransform;

    /// <summary>
    /// Enables the controllers to directly interact with the keyboard.
    /// </summary>
    public bool controllerDirectInteraction = true;

    /// <summary>
    /// Enables the controllers to send ray interactions to the keyboard.
    /// </summary>
    public bool controllerRayInteraction = true;

    /// <summary>
    /// Configures the raycast mask used when sending raycast controller input to the keyboard.
    /// </summary>
    public OVRPhysicsRaycaster controllerRaycaster;

    [Header("Hand Input")]
    /// <summary>
    /// The OVRHand representing the left hand. Requires the OVRHand to also have an OVRSkeleton.
    /// </summary>
    public OVRHand handLeft;

    /// <summary>
    /// The OVRHand representing the right hand. Requires the OVRHand to also have an OVRSkeleton.
    /// </summary>
    public OVRHand handRight;

    /// <summary>
    /// Enables tracked hands to directly interact with the keyboard.
    /// </summary>
    public bool handDirectInteraction = true;

    /// <summary>
    /// Enables tracked hands to send ray interactions to the keyboard.
    /// </summary>
    public bool handRayInteraction = true;

    /// <summary>
    /// Configures the raycast mask used when sending raycast hand input to the keyboard.
    /// </summary>
    public OVRPhysicsRaycaster handRaycaster;

    [Header("Graphics")]
    /// <summary>
    /// The shader used to render the keyboard’s glTF materials.
    /// </summary>
    public Shader keyboardModelShader;

    /// <summary>
    /// The shader used to render the keyboard’s glTF alpha blended materials.
    /// </summary>
    public Shader keyboardModelAlphaBlendShader;

    /// <summary>
    /// If false, prevents all keyboard input.
    /// </summary>
    [NonSerialized]
    public bool InputEnabled = true;

    [Serializable]
    public class CommitTextUnityEvent : UnityEvent<string> { }

    [Header("Event Handling")]
    public CommitTextUnityEvent CommitTextEvent = new CommitTextUnityEvent();
    public UnityEvent BackspaceEvent = new UnityEvent();
    public UnityEvent EnterEvent = new UnityEvent();
    public UnityEvent KeyboardShownEvent = new UnityEvent();
    public UnityEvent KeyboardHiddenEvent = new UnityEvent();

    private bool isKeyboardCreated_ = false;

    private UInt64 keyboardSpace_;

    private Dictionary<ulong, VirtualKeyboardTextureInfo> virtualKeyboardTextures_ = new Dictionary<ulong, VirtualKeyboardTextureInfo>();

    private OVRGLTFScene virtualKeyboardScene_;
    private UInt64 virtualKeyboardModelKey_;
    private bool modelInitialized_ = false;
    private bool modelAvailable_ = false;
    private bool keyboardVisible_ = false;
    private InteractorRootTransformOverride _interactorRootTransformOverride = new InteractorRootTransformOverride();
    private List<IInputSource> _inputSources;

    // Used to ignore internal invokes of OnValueChanged without unbinding/rebinding
    private KeyboardEventListener keyboardEventListener_;
    private Coroutine gltfModelCoroutine_;
    private OVRGLTFLoader _gltfLoader;

    private int _animationStateCount;
    private int _animationStateBufferLength;
    private IntPtr _animationStateBuffer;

    [Obsolete("TextCommitField has been replaced with TextHandler for more flexibility.")]
    public InputField TextCommitField
    {
        get
        {
            Debug.LogWarning("Migrate to TextHandler for better performance.");
            return (textHandler as OVRVirtualKeyboardInputFieldTextHandler)?.InputField;
        }
        set
        {
            Debug.LogWarning("Migrate to TextHandler for better performance.");
            if (TextHandler is OVRVirtualKeyboardInputFieldTextHandler handler)
            {
                handler.InputField = value;
            }
        }
    }

    // ensures runtime updates to the TextHandler keep text context in sync
    public ITextHandler TextHandler
    {
        get => _runtimeTextHandler;
        set
        {
            if (_runtimeTextHandler == value)
            {
                return;
            }

            if (_runtimeTextHandler != null)
            {
                _runtimeTextHandler.OnTextChanged -= OnTextHandlerChange;
            }

            _runtimeTextHandler = value;
            if (_runtimeTextHandler != null)
            {
                _runtimeTextHandler.OnTextChanged += OnTextHandlerChange;
                ChangeTextContextInternal(_runtimeTextHandler.Text);
            }
        }
    }

    // Unity event functions
    void Awake()
    {
        if (keyboardModelShader == null)
        {
            Debug.LogWarning($"keyboardModelShader not specified; falling back to {_defaultShaderName}");
            keyboardModelShader = Shader.Find(_defaultShaderName);
        }

        if (keyboardModelAlphaBlendShader == null)
        {
            Debug.LogWarning($"keyboardModelAlphaBlendShader not specified; falling back to {_defaultAlphaBlendShaderName}");
            keyboardModelAlphaBlendShader = Shader.Find(_defaultAlphaBlendShaderName);
        }

        if (singleton_ != null)
        {
            GameObject.Destroy(this);
            throw new Exception("OVRVirtualKeyboard only supports a single instance");
        }

        if (leftControllerDirectTransform == null && leftControllerRootTransform != null)
        {
            if (controllerDirectInteraction)
            {
                Debug.LogWarning("Missing left controller direct transform for virtual keyboard input; falling back to the root!");
            }
            leftControllerDirectTransform = leftControllerRootTransform;
        }

        if (rightControllerDirectTransform == null && rightControllerRootTransform != null)
        {
            if (controllerDirectInteraction)
            {
                Debug.LogWarning("Missing right controller direct transform for virtual keyboard input; falling back to the root!");
            }
            rightControllerDirectTransform = rightControllerRootTransform;
        }

        singleton_ = this;
        if (OVRManager.instance)
        {
            keyboardEventListener_ = new KeyboardEventListener(this);
            OVRManager.instance.RegisterEventListener(keyboardEventListener_);
        }

        // Initialize serialized text commit field
        TextHandler = textHandler;

        // Register for events
        CommitTextEvent.AddListener(OnCommitText);
        BackspaceEvent.AddListener(OnBackspace);
        EnterEvent.AddListener(OnEnter);
        KeyboardShownEvent.AddListener(OnKeyboardShown);
        KeyboardHiddenEvent.AddListener(OnKeyboardHidden);
    }

    void OnDestroy()
    {
        if (!OVRPlugin.initialized)
        {
            return;
        }
        CommitTextEvent.RemoveListener(OnCommitText);
        BackspaceEvent.RemoveListener(OnBackspace);
        EnterEvent.RemoveListener(OnEnter);
        KeyboardShownEvent.RemoveListener(OnKeyboardShown);
        KeyboardHiddenEvent.RemoveListener(OnKeyboardHidden);

        foreach (var texture in virtualKeyboardTextures_.Values)
        {
            if (texture.buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(texture.buffer);
            }
        }
        virtualKeyboardTextures_.Clear();

        TextHandler = null;
        if (singleton_ == this)
        {
            if (OVRManager.instance != null)
            {
                OVRManager.instance.DeregisterEventListener(keyboardEventListener_);
            }
            singleton_ = null;
        }
        keyboardEventListener_ = null;

        DestroyKeyboard();
    }

    void OnEnable()
    {
        ShowKeyboard();
    }

    void OnDisable()
    {
        if (!OVRPlugin.initialized)
        {
            return;
        }
        HideKeyboard();
    }

    private void Reset()
    {
        keyboardModelShader = Shader.Find(_defaultShaderName);
        keyboardModelAlphaBlendShader = Shader.Find(_defaultAlphaBlendShaderName);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Handle migration from obsolete textCommitField to textHandler
#pragma warning disable CS0612
#pragma warning disable CS0618
        if (textCommitField != null && textHandler != null)
        {
            Debug.Log("Migrating Text Commit Field to Unity Input Field Text Handler", this);
            TextHandler = textHandler;
            TextCommitField = textCommitField;
            textCommitField = null;
        }
#pragma warning restore CS0618
#pragma warning restore CS0612

        transform.hideFlags = (InitialPosition == KeyboardPosition.Custom) ? HideFlags.None : HideFlags.NotEditable;
    }

    private void OnDrawGizmos()
    {
        if (enabled && !modelAvailable_)
        {
            // The keyboard model is a runtime loaded GLTF file
            // For Editor testing without Link, draw a simple keyboard representation gizmo

            // Use approximate positions for Far/Near, not guaranteed match Oculus Runtime
            Vector3 position;
            Quaternion rotation;
            Vector3 scale;
            switch (InitialPosition)
            {
                case KeyboardPosition.Far:
                    position = new Vector3(0, -0.5f, 1f);
                    rotation = Quaternion.identity;
                    scale = Vector3.one;
                    break;
                case KeyboardPosition.Near:
                    position = new Vector3(0, -0.4f, 0.4f);
                    rotation = Quaternion.Euler(65, 0, 0);
                    scale = Vector3.one * 0.4f;
                    break;
                case KeyboardPosition.Custom:
                default:
                    position = transform.position;
                    rotation = transform.rotation;
                    scale = transform.lossyScale;
                    break;
            }
            Gizmos.matrix = Matrix4x4.TRS(position, rotation, scale);

            // Draw Keyboard Background
            Gizmos.color = new Color(0.9f, 1, 0.9f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(0, 0, 0.005f), new Vector3(1.0f, 0.4f, 0.01f));
            // Draw Spacebar Key
            Gizmos.color = new Color(0.9f, 1, 0.9f, 0.4f);
            Gizmos.DrawWireCube(new Vector3(0, -0.13f, 0), new Vector3(0.6f, 0.08f, 0));
        }
    }
#endif

    // public functions
    /// <summary>
    /// Updates the keyboard to a reference position.
    /// </summary>
    public void UseSuggestedLocation(KeyboardPosition position)
    {
        OVRPlugin.VirtualKeyboardLocationInfo locationInfo = new OVRPlugin.VirtualKeyboardLocationInfo();
        switch (position)
        {
            case KeyboardPosition.Near:
                locationInfo.locationType = OVRPlugin.VirtualKeyboardLocationType.Direct;
                break;
            case KeyboardPosition.Far:
                locationInfo.locationType = OVRPlugin.VirtualKeyboardLocationType.Far;
                break;
            case KeyboardPosition.Custom:
                locationInfo = ComputeLocation(transform);
                break;
            default:
                Debug.LogError("Unknown KeyboardInputMode: " + position);
                return;
        }
        InitialPosition = position;
        if (keyboardSpace_ == 0)
        {
            // If KB space is not yet created no need to update the runtime
            return;
        }
        locationInfo.trackingOriginType = OVRPlugin.GetTrackingOriginType();
        var result = OVRPlugin.SuggestVirtualKeyboardLocation(locationInfo);
        if (result != OVRPlugin.Result.Success)
        {
            Debug.LogError("SuggestVirtualKeyboardLocation failed: " + result);
            return;
        }
        // Clear transform has changed state to avoid a custom location overriding the SuggestVirtualKeyboardLocation call
        transform.hasChanged = false;
        SyncKeyboardLocation();
    }

    /// <summary>
    /// Sends a ray input to the keyboard from a given transform.
    /// </summary>
    /// <param name="inputTransform">GameObject Transform with the pose and forward orientation of the input ray.</param>
    /// <param name="source">Input source to use (ex. Controller/Hand Left/Right).</param>
    /// <param name="isPressed">If true, will trigger a key press if the ray collides with a keyboard key.</param>
    /// <param name="useRaycastMask">Defaults to true. Will use the configured raycast mask for the given input source.</param>
    public void SendVirtualKeyboardRayInput(Transform inputTransform,
        InputSource source, bool isPressed, bool useRaycastMask = true)
    {
        var inputSource = source switch
        {
            InputSource.ControllerLeft => OVRPlugin.VirtualKeyboardInputSource.ControllerRayLeft,
            InputSource.ControllerRight => OVRPlugin.VirtualKeyboardInputSource.ControllerRayRight,
            InputSource.HandLeft => OVRPlugin.VirtualKeyboardInputSource.HandRayLeft,
            InputSource.HandRight => OVRPlugin.VirtualKeyboardInputSource.HandRayRight,
            _ => throw new Exception("Unknown input source: " + source)
        };

        var raycaster = (source == InputSource.ControllerLeft || source == InputSource.ControllerRight)
            ? controllerRaycaster
            : handRaycaster;

        if (raycaster)
        {
            var pointerData = new OVRPointerEventData(EventSystem.current)
            {
                worldSpaceRay = new Ray(inputTransform.position, inputTransform.forward)
            };
            var results = new List<RaycastResult>();
            raycaster.Raycast(pointerData, results);
            if (results.Count <= 0 || results[0].gameObject != Collider.gameObject)
            {
                return;
            }
        }
        SendVirtualKeyboardInput(inputSource, inputTransform.ToOVRPose(), isPressed);
    }

    /// <summary>
    /// Sends a direct input to the keyboard from a given transform.
    /// </summary>
    /// <param name="position">The collision point which is interacting with the keyboard. For example, a hand index finger tip.</param>
    /// <param name="source">The input source to use (ex. Controller/Hand Left/Right).</param>
    /// <param name="isPressed">If the input is triggering a press or not.</param>
    public void SendVirtualKeyboardDirectInput(Vector3 position,
        InputSource source, bool isPressed, Transform interactorRootTransform = null)
    {
        var inputSource = source switch
        {
            InputSource.ControllerLeft => OVRPlugin.VirtualKeyboardInputSource.ControllerDirectLeft,
            InputSource.ControllerRight => OVRPlugin.VirtualKeyboardInputSource.ControllerDirectRight,
            InputSource.HandLeft => OVRPlugin.VirtualKeyboardInputSource.HandDirectIndexTipLeft,
            InputSource.HandRight => OVRPlugin.VirtualKeyboardInputSource.HandDirectIndexTipRight,
            _ => throw new Exception("Unknown input source: " + source)
        };
        SendVirtualKeyboardInput(inputSource, new OVRPose()
        {
            position = position
        }, isPressed, interactorRootTransform);
    }

    /// <summary>
    /// Enables custom handling of text context. Use this when changing input fields or if the input text has changed via another script.
    /// </summary>
    public void ChangeTextContext(string textContext)
    {
        if (TextHandler != null && TextHandler.Text != textContext)
        {
            Debug.LogWarning("TextHandler text out of sync with Keyboard text context");
        }

        ChangeTextContextInternal(textContext);
    }

    // Private methods
    private void LoadRuntimeVirtualKeyboardMesh()
    {
        modelAvailable_ = false;
        gltfModelCoroutine_ = StartCoroutine(InitializeGlTFModel());
    }

    private IEnumerator InitializeGlTFModel()
    {
        Func<MemoryStream> loadMeshData = () =>
        {
            Debug.Log("LoadRuntimeVirtualKeyboardMesh");
            string[] modelPaths = OVRPlugin.GetRenderModelPaths();

            var keyboardPath = modelPaths?.FirstOrDefault(p => p.Equals("/model_fb/virtual_keyboard")
                                                               || p.Equals("/model_meta/keyboard/virtual"));

            if (String.IsNullOrEmpty(keyboardPath))
            {
                Debug.LogError("Failed to find keyboard model.  Check Render Model support.");
                return null;
            }

            OVRPlugin.RenderModelProperties modelProps = new OVRPlugin.RenderModelProperties();
            if (!OVRPlugin.GetRenderModelProperties(keyboardPath, ref modelProps))
            {
                Debug.LogError("Failed to find keyboard model properties.  Check Render Model support.");
                return null;
            }

            if (modelProps.ModelKey == OVRPlugin.RENDER_MODEL_NULL_KEY)
            {
                Debug.LogError("Failed to find keyboard model key.  Check Render Model support.");
                return null;
            }
            virtualKeyboardModelKey_ = modelProps.ModelKey;
            return new MemoryStream(OVRPlugin.LoadRenderModel(modelProps.ModelKey));
        };

        _gltfLoader = new OVRGLTFLoader(loadMeshData);

        _gltfLoader.textureUriHandler = (string rawUri, Material mat) =>
        {
            var uri = new Uri(rawUri);
            // metaVirtualKeyboard://texture/{id}?w={width}&h={height}&ft=RGBA32
            if (!uri.Scheme.Equals("metaVirtualKeyboard", StringComparison.OrdinalIgnoreCase) || uri.Host != "texture")
            {
                return null;
            }
            var textureId = ulong.Parse(uri.LocalPath.Substring(1));

            VirtualKeyboardTextureInfo texture;
            if (!virtualKeyboardTextures_.TryGetValue(textureId, out texture))
            {
                texture.materials = new List<Material>();
            }
            texture.materials.Add(mat);
            virtualKeyboardTextures_[textureId] = texture;
            return null; // defer texture data loading
        };
        _gltfLoader.SetModelShader(keyboardModelShader);
        _gltfLoader.SetModelAlphaBlendShader(keyboardModelAlphaBlendShader);

        var loadGlbCoroutine = _gltfLoader.LoadGLBCoroutine(supportAnimation: true, loadMips: true);
        while (loadGlbCoroutine.MoveNext())
        {
            yield return loadGlbCoroutine.Current;
        }
        virtualKeyboardScene_ = _gltfLoader.scene;
        _gltfLoader = null;
        gltfModelCoroutine_ = null;
        modelAvailable_ = virtualKeyboardScene_.root != null;
        if (modelAvailable_)
        {
            virtualKeyboardScene_.root.transform.SetParent(transform, false);
            virtualKeyboardScene_.root.gameObject.name = "OVRVirtualKeyboardModel";
            // keyboard is not intended for modification
            ApplyHideFlags(virtualKeyboardScene_.root.transform);
            SetKeyboardVisibility(true);
            UseSuggestedLocation(InitialPosition);
            UpdateAnimationState();
            PopulateCollision();
        }
    }

    private static void ApplyHideFlags(Transform t)
    {
        t.gameObject.hideFlags = HideFlags.NotEditable | HideFlags.DontSave;
        for (int i = 0; i < t.childCount; i++)
        {
            ApplyHideFlags(t.GetChild(i));
        }
    }

    private void PopulateCollision()
    {
        if (!modelAvailable_)
        {
            throw new Exception("Keyboard Model Unavailable");
        }

        var childrenMeshes = virtualKeyboardScene_.root.GetComponentsInChildren<MeshFilter>();
        var collisionMesh = childrenMeshes.Where(mesh => mesh.gameObject.name == "collision").FirstOrDefault();
        if (collisionMesh != null)
        {
            var meshCollider = collisionMesh.gameObject.AddComponent<MeshCollider>();
            meshCollider.convex = true;
            Collider = meshCollider;

            // collision mesh shouldn't need to be rendered
            var renderer = collisionMesh.gameObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }
    }

    private void ShowKeyboard()
    {
        if (!isKeyboardCreated_)
        {
            var createInfo = new OVRPlugin.VirtualKeyboardCreateInfo();
            var result = OVRPlugin.CreateVirtualKeyboard(createInfo);
            if (result != OVRPlugin.Result.Success)
            {
#if UNITY_EDITOR
                if (result == OVRPlugin.Result.Failure_Unsupported || result == OVRPlugin.Result.Failure_NotInitialized)
                {
                    Debug.LogWarning("Virtual Keyboard Unity Editor support requires Quest Link.");
                }
                else
#endif
                {
                    Debug.LogError("Create failed: '" + result + "'. Check for Virtual Keyboard Support.");
                }
                return;
            }

            isKeyboardCreated_ = true;
        }

        // Initialize the keyboard model
        if (modelInitialized_ != true)
        {
            modelInitialized_ = true;
            LoadRuntimeVirtualKeyboardMesh();
        }
        else
        {
            SetKeyboardVisibility(true);
        }

        // Should call this whenever the keyboard is enabled or when the text focus changes
        if (TextHandler != null)
        {
            ChangeTextContextInternal(TextHandler.Text);
        }
    }

    private void SetKeyboardVisibility(bool visible)
    {
        if (!modelAvailable_)
        {
            // Set active was called before the model was loaded
            return;
        }

        var visibility = new OVRPlugin.VirtualKeyboardModelVisibility();
        visibility.Visible = visible;
        var res = OVRPlugin.SetVirtualKeyboardModelVisibility(ref visibility);
        if (res != OVRPlugin.Result.Success)
        {
            Debug.LogError("SetVirtualKeyboardModelVisibility failed: " + res);
        }
    }

    private void HideKeyboard()
    {
        if (modelInitialized_ && !modelAvailable_)
        {
            UnloadModel();
        }
        SetKeyboardVisibility(false);
    }

    private void UnloadModel()
    {
        if (gltfModelCoroutine_ != null)
        {
            StopCoroutine(gltfModelCoroutine_);
            gltfModelCoroutine_ = null;
        }
        if (_gltfLoader != null && _gltfLoader.scene.root != null)
        {
            Destroy(_gltfLoader.scene.root);
            _gltfLoader = null;
        }
        if (modelAvailable_)
        {
            GameObject.Destroy(virtualKeyboardScene_.root);
            modelAvailable_ = false;
        }
        modelInitialized_ = false;
    }

    private void DestroyKeyboard()
    {
        UnloadModel();
        InputEnabled = false;
        if (isKeyboardCreated_)
        {
            var result = OVRPlugin.DestroyVirtualKeyboard();
            if (result != OVRPlugin.Result.Success)
            {
                Debug.LogError("Destroy failed");
            }
            else
            {
                Debug.Log("Destroy success");
            }
            isKeyboardCreated_ = false;
        }
        if (_inputSources != null)
        {
            foreach (var inputSource in _inputSources)
            {
                inputSource.Dispose();
            }
        }
        _inputSources = null;
    }

    private float MaxElement(Vector3 vec)
    {
        return Mathf.Max(Mathf.Max(vec.x, vec.y), vec.z);
    }

    private OVRPlugin.VirtualKeyboardLocationInfo ComputeLocation(Transform transform)
    {
        OVRPlugin.VirtualKeyboardLocationInfo location = new OVRPlugin.VirtualKeyboardLocationInfo();

        location.locationType = OVRPlugin.VirtualKeyboardLocationType.Custom;
        // Plane in Unity has its normal facing towards camera by default, in runtime it's facing away,
        // so to compensate, flip z for both position and rotation, for both plane and pointer pose.
        location.pose.Position = transform.position.ToFlippedZVector3f();
        location.pose.Orientation = transform.rotation.ToFlippedZQuatf();
        location.scale = MaxElement(transform.localScale);
        return location;
    }

    void Update()
    {
        if (!OVRPlugin.initialized)
        {
            return;
        }
        if (modelAvailable_)
        {
            UpdateInputs();
        }
        if (isKeyboardCreated_)
        {
            SyncKeyboardLocation();
        }
        if (modelAvailable_)
        {
            UpdateAnimationState();
        }
    }

    private void LateUpdate()
    {
        _interactorRootTransformOverride.LateApply(this);
    }

    private void SendVirtualKeyboardInput(OVRPlugin.VirtualKeyboardInputSource inputSource, OVRPose pose,
        bool isPressed, Transform interactorRootTransform = null)
    {
        var inputInfo = new OVRPlugin.VirtualKeyboardInputInfo();
        inputInfo.inputTrackingOriginType = (OVRPlugin.TrackingOrigin)OVRManager.instance.trackingOriginType;
        inputInfo.inputSource = inputSource;
        inputInfo.inputPose = pose.ToPosef();
        inputInfo.inputState = (isPressed) ? OVRPlugin.VirtualKeyboardInputStateFlags.IsPressed : 0;
        var hasInteractorRootTransform = interactorRootTransform != null;
        var interactorRootPose = (!hasInteractorRootTransform)
            ? pose.ToPosef()
            : interactorRootTransform.ToOVRPose().ToPosef();
        var result = OVRPlugin.SendVirtualKeyboardInput(inputInfo, ref interactorRootPose);
        if (result != OVRPlugin.Result.Success)
        {
#if DEVELOPMENT_BUILD
            Debug.LogError("Failed to send input source " + inputSource);
#endif
            return;
        }

        if (interactorRootTransform != null)
        {
            _interactorRootTransformOverride.Enqueue(interactorRootTransform, interactorRootPose);
        }
    }

    private void UpdateInputs()
    {
        if (!InputEnabled || !modelAvailable_)
        {
            return;
        }

        if (_inputSources == null)
        {
            _inputSources = new List<IInputSource>();
            if (leftControllerRootTransform)
            {
                _inputSources.Add(new ControllerInputSource(this, InputSource.ControllerLeft, OVRInput.Controller.LTouch,
                    leftControllerRootTransform, leftControllerDirectTransform));
            }
            if (rightControllerRootTransform)
            {
                _inputSources.Add(new ControllerInputSource(this, InputSource.ControllerRight, OVRInput.Controller.RTouch,
                    rightControllerRootTransform, rightControllerDirectTransform));
            }
            if (handLeft)
            {
                _inputSources.Add(new HandInputSource(this, InputSource.HandLeft, handLeft));
            }
            if (handRight)
            {
                _inputSources.Add(new HandInputSource(this, InputSource.HandRight, handRight));
            }
        }
        foreach (var inputSource in _inputSources)
        {
            inputSource.Update();
        }
    }

    private ulong GetKeyboardSpace()
    {
        if (keyboardSpace_ != 0)
        {
            return keyboardSpace_;
        }
        var createSpaceInfo = new OVRPlugin.VirtualKeyboardSpaceCreateInfo();
        var locationInfo = ComputeLocation(transform);
        createSpaceInfo.locationType = locationInfo.locationType;
        createSpaceInfo.trackingOriginType = OVRPlugin.GetTrackingOriginType();
        createSpaceInfo.pose = locationInfo.pose;
        var result = OVRPlugin.CreateVirtualKeyboardSpace(createSpaceInfo, out keyboardSpace_);
        if (result != OVRPlugin.Result.Success)
        {
            Debug.LogError("Create failed to create keyboard space: " + result);
            DestroyKeyboard();
        }
        UseSuggestedLocation(InitialPosition);
        return keyboardSpace_;
    }

    private void SyncKeyboardLocation()
    {
        // If unity transform has updated, sync with runtime
        if (keyboardSpace_ != 0 && transform.hasChanged)
        {
            // ensure scale uniformity
            var scale = MaxElement(transform.localScale);
            var maxScale = Vector3.one * scale;
            transform.localScale = maxScale;
            UseSuggestedLocation(KeyboardPosition.Custom);
        }

        // query the runtime for the true position
        if (!OVRPlugin.TryLocateSpace(GetKeyboardSpace(), OVRPlugin.GetTrackingOriginType(), out var keyboardPose))
        {
            Debug.LogError("Failed to locate the virtual keyboard space.");
            return;
        }

        var result = OVRPlugin.GetVirtualKeyboardScale(out var keyboardScale);
        if (result != OVRPlugin.Result.Success)
        {
            Debug.LogError("Failed to get virtual keyboard scale.");
            return;
        }

        Transform keyboardTransform = transform;
        keyboardTransform.SetPositionAndRotation(
            keyboardPose.Position.FromFlippedZVector3f(),
            keyboardPose.Orientation.FromFlippedZQuatf());
        keyboardTransform.localScale = Vector3.one * keyboardScale;
        // Reset the change flag to prevent recursive updates
        keyboardTransform.hasChanged = false;
    }

    private void UpdateAnimationState()
    {
        if (!modelAvailable_)
        {
            return;
        }
        OVRPlugin.GetVirtualKeyboardDirtyTextures(out var dirtyTextures);
        foreach (var textureId in dirtyTextures.TextureIds)
        {

            if (!virtualKeyboardTextures_.TryGetValue(textureId, out var textureInfo))
            {
                continue;
            }

            // Query texture details
            var textureData = new OVRPlugin.VirtualKeyboardTextureData();
            OVRPlugin.GetVirtualKeyboardTextureData(textureId, ref textureData);
            // Access the texture data
            if (textureData.BufferCountOutput > 0)
            {
                if (textureInfo.bufferLength < textureData.BufferCountOutput && textureInfo.buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(textureInfo.buffer);
                    textureInfo.buffer = IntPtr.Zero;
                }
                if (textureInfo.buffer == IntPtr.Zero)
                {
                    textureInfo.bufferLength = textureData.BufferCountOutput;
                    textureInfo.buffer = Marshal.AllocHGlobal((int)textureData.BufferCountOutput);
                }
                textureData.Buffer = textureInfo.buffer;
                textureData.BufferCapacityInput = textureInfo.bufferLength;

                OVRPlugin.GetVirtualKeyboardTextureData(textureId, ref textureData);
                if (textureInfo.hasTexture)
                {
                    if (textureInfo.texture.width != textureData.TextureWidth ||
                        textureInfo.texture.height != textureData.TextureHeight)
                    {
                        textureInfo.hasTexture = false;
                    }
                }
                if (!textureInfo.hasTexture)
                {
                    textureInfo.texture = new Texture2D((int)textureData.TextureWidth,
                        (int)textureData.TextureHeight,
                        TextureFormat.RGBA32, false);
                    textureInfo.texture.filterMode = FilterMode.Trilinear;
                    textureInfo.hasTexture = true;
                }
                textureInfo.texture.LoadRawTextureData(textureInfo.buffer,
                    (int)textureInfo.bufferLength);
                textureInfo.texture.Apply(true /*updateMipmaps*/, false /*makeNoLongerReadable*/);
                virtualKeyboardTextures_[textureId] = textureInfo;
                foreach (var material in textureInfo.materials)
                {
                    material.mainTexture = textureInfo.texture;
                }
            }
        }


        // reset animation count
        _animationStateCount = 0;
        OVRPlugin.GetVirtualKeyboardModelAnimationStates(AnimationStatesBufferProvider, AnimationStateHandler);

        if (_animationStateCount > 0)
        {
            foreach (var morphTargets in virtualKeyboardScene_.morphTargetHandlers)
            {
                morphTargets.Update();
            }
        }
    }

    IntPtr AnimationStatesBufferProvider(int bufferLength, int count)
    {
        if (_animationStateBufferLength < bufferLength)
        {
            Marshal.FreeHGlobal(_animationStateBuffer);
            _animationStateBufferLength = bufferLength;
            _animationStateBuffer = Marshal.AllocHGlobal(_animationStateBufferLength);
        }

        _animationStateCount = count;
        return _animationStateBuffer;
    }

    void AnimationStateHandler(ref OVRPlugin.VirtualKeyboardModelAnimationState state)
    {
        if (state.AnimationIndex >= virtualKeyboardScene_.animationNodeLookup.Count)
        {
            Debug.LogWarning($"Unknown Animation State Index {state.AnimationIndex}");
            return;
        }

        var animationNodes = virtualKeyboardScene_.animationNodeLookup[state.AnimationIndex];
        for (var i = 0; i < animationNodes.Length; i++)
        {
            animationNodes[i].UpdatePose(state.Fraction, false);
        }
    }

    private void OnCommitText(string text)
    {
        if (TextHandler == null)
        {
            return;
        }

        using var textScope = new TextHandlerScope(TextHandler, OnTextHandlerChange);
        textScope.AppendText(text);
    }

    private void OnTextHandlerChange(string textContext)
    {
        ChangeTextContextInternal(textContext);
    }

    private void ChangeTextContextInternal(string textContext)
    {
        if (!isKeyboardCreated_)
        {
            return;
        }

        var result = OVRPlugin.ChangeVirtualKeyboardTextContext(textContext);
        if (result != OVRPlugin.Result.Success)
        {
            Debug.LogError("Failed to set keyboard text context");
        }
    }

    private void OnBackspace()
    {
        if (TextHandler == null)
        {
            return;
        }
        using var textScope = new TextHandlerScope(TextHandler, OnTextHandlerChange);
        textScope.ApplyBackspace();
    }

    private void OnEnter()
    {
        if (TextHandler == null)
        {
            return;
        }

        using var textScope = new TextHandlerScope(TextHandler, OnTextHandlerChange);
        if (textScope.SubmitOnEnter)
        {
            textScope.Submit();
        }
        else
        {
            OnCommitText("\n");
        }
    }

    private void OnKeyboardShown()
    {
        if (!keyboardVisible_)
        {
            keyboardVisible_ = true;
            UpdateVisibleState();
        }
    }

    private void OnKeyboardHidden()
    {
        if (keyboardVisible_)
        {
            keyboardVisible_ = false;
            UpdateVisibleState();
        }
    }

    private void UpdateVisibleState()
    {
        gameObject.SetActive(keyboardVisible_);
        if (modelAvailable_)
        {
            virtualKeyboardScene_.root.gameObject.SetActive(keyboardVisible_);
        }
    }

    [ContextMenu("Autofill Input Roots")]
    public void AutoPopulate()
    {
        OVRCameraRig camRig = FindAnyObjectByType<OVRCameraRig>();
        if (camRig == null)
        {
            Debug.LogWarning("Couldn't auto fill input transforms as we didn't have an OVRCameraRig.");
            return;
        }
        if (handRight == null || handLeft == null)
        {
            OVRHand[] hands = camRig.GetComponentsInChildren<OVRHand>();
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].HandType == OVRHand.Hand.HandLeft && handLeft == null)
                {
                    handLeft = hands[i];
                }
                if (hands[i].HandType == OVRHand.Hand.HandRight && handRight == null)
                {
                    handRight = hands[i];
                }

                if (handRight && handLeft)
                {
                    break;
                }
            }
        }

        if (leftControllerRootTransform == null || rightControllerRootTransform == null)
        {
            OVRControllerHelper[] controllers = camRig.GetComponentsInChildren<OVRControllerHelper>();

            for (int i = 0; i < controllers.Length; i++)
            {
                if (leftControllerRootTransform == null && controllers[i].m_controller == OVRInput.Controller.LTouch)
                {
                    leftControllerRootTransform = controllers[i].transform;
                }

                if (rightControllerRootTransform == null && controllers[i].m_controller == OVRInput.Controller.RTouch)
                {
                    rightControllerRootTransform = controllers[i].transform;
                }

                if (leftControllerRootTransform && rightControllerRootTransform)
                {
                    break;
                }
            }
        }
    }
}
