using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Input;
using YARG.Gameplay;
using YARG.Gameplay.Player;
using YARG.Helpers;
using YARG.Settings;

namespace YARG.Input.Touch
{
    /// <summary>
    /// Builds the Android touch controls (5 frets + strum bar) entirely in
    /// code — no scene or prefab edits are needed. The bootstrap
    /// (<see cref="TouchControlsBootstrap"/>) adds this component in the
    /// Gameplay scene on Android.
    ///
    /// Input path (verified against v0.15.0): each button injects a real
    /// <see cref="YARG.Core.Input.GameInput"/> through the player's own
    /// <see cref="ControlBinding"/> (see
    /// <see cref="ControlBinding.InjectInput"/>), so the input is calibrated,
    /// queued by the engine and recorded in replays exactly like a physical
    /// guitar. Fret = hold (true on press, false on release), strum = trigger
    /// (true on press, StrumDown only).
    ///
    /// Layout: every button can be dragged to a new position in edit mode,
    /// and the selected button can be resized with the + / - buttons.
    /// Positions and sizes are normalized (0..1 of the screen) and saved to
    /// <c>touchLayout.json</c> next to YARG's <c>settings.json</c>.
    /// <para>
    /// NOTE: this is a plain <see cref="MonoBehaviour"/>, deliberately NOT a
    /// <c>GameplayBehaviour</c>. That base class destroys the object in
    /// <c>Awake</c> when no GameManager is found yet, and the Gameplay scene
    /// is loaded additively — so a timing miss meant the touch controls never
    /// appeared at all (build #15 phone test: no buttons, no EDIT toggle).
    /// The GameManager is resolved lazily here instead, only when input is
    /// injected or the pause state is checked.
    /// </para>
    /// </summary>
    public class TouchControlsManager : MonoBehaviour
    {
        private const float MIN_SCALE = 0.5f;
        private const float MAX_SCALE = 2.0f;
        private const float SCALE_STEP = 1.1f;

        // Lazily resolved: the GameManager is a scene object and may not
        // exist yet when this manager is created (or ever, outside gameplay).
        private GameManager _gameManager;
        private GameManager GameManager
        {
            get
            {
                if (_gameManager == null)
                    _gameManager = FindAnyObjectByType<GameManager>();
                return _gameManager;
            }
        }

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private readonly List<TouchFretButton> _buttons = new List<TouchFretButton>();
        private readonly Dictionary<string, ControlBinding> _bindings = new Dictionary<string, ControlBinding>();

        private TouchLayoutData _layout;
        private TouchFretButton _selectedButton;

        private bool _editMode;
        private GameObject _editPanel;
        private GameObject _editToggleButton;

        private static readonly Color[] FretColors =
        {
            new Color(0.15f, 0.85f, 0.25f, 0.75f), // Green
            new Color(0.95f, 0.20f, 0.20f, 0.75f), // Red
            new Color(0.95f, 0.85f, 0.15f, 0.75f), // Yellow
            new Color(0.20f, 0.45f, 0.95f, 0.75f), // Blue
            new Color(0.95f, 0.50f, 0.10f, 0.75f), // Orange
        };

        private static readonly Color StrumColor = new Color(0.90f, 0.90f, 0.90f, 0.60f);

        private static readonly string[] ButtonOrder =
        {
            TouchButtonIds.FretGreen,
            TouchButtonIds.FretRed,
            TouchButtonIds.FretYellow,
            TouchButtonIds.FretBlue,
            TouchButtonIds.FretOrange,
            TouchButtonIds.Strum,
        };

        private void Awake()
        {
            // Keep the screen on during songs (phones love to sleep mid-song).
            if (Application.platform == RuntimePlatform.Android)
                Screen.sleepTimeout = SleepTimeout.NeverSleep;

            ApplyAndroidFirstRunDefaults();
            BuildUi();
        }

        private void OnDestroy()
        {
            SaveLayout();
            if (Application.platform == RuntimePlatform.Android)
                Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }

        private void Update()
        {
            // Never let the touch buttons sit on top of the pause menu.
            // (GameManager is null before the song is set up — then there is
            // no pause menu either, so keep the canvas visible.)
            if (_canvas != null)
            {
                var gm = GameManager;
                _canvas.enabled = gm == null || !gm.Paused;
            }
        }

        // ------------------------------------------------------------------
        // UI construction (code-only, no prefabs)
        // ------------------------------------------------------------------

        private void BuildUi()
        {
            _layout = TouchLayoutData.Load();

            var canvasObject = new GameObject("TouchControlsCanvas");
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Well above the game's own HUD canvases (sortingOrder 0) so the
            // buttons — and the EDIT toggle — are never hidden behind the
            // score box or other HUD elements. The canvas is hidden while
            // paused (see Update), so the pause menu stays reachable.
            _canvas.sortingOrder = 100;
            _canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.AddComponent<GraphicRaycaster>();

            for (int i = 0; i < ButtonOrder.Length; i++)
            {
                string id = ButtonOrder[i];
                var entry = _layout.GetButton(id);
                if (entry == null)
                    continue;

                var buttonObject = new GameObject("TouchButton_" + id);
                buttonObject.transform.SetParent(_canvasRect, false);
                var image = buttonObject.AddComponent<Image>();
                image.raycastTarget = true;
                var button = buttonObject.AddComponent<TouchFretButton>();
                button.Setup(id, id == TouchButtonIds.Strum ? StrumColor : FretColors[i]);
                button.PressedChanged += OnButtonPressedChanged;
                button.Dragged += OnButtonDragged;
                button.TappedInEditMode += OnButtonTappedInEditMode;
                _buttons.Add(button);
            }

            ApplyLayoutToButtons();
            BuildEditUi();
        }

        private void BuildEditUi()
        {
            // Small EDIT toggle, top-right. Always visible during gameplay.
            _editToggleButton = CreateTextButton("EDIT", new Vector2(0.96f, 0.95f),
                new Vector2(90f, 44f), () => SetEditMode(!_editMode));
            _editToggleButton.transform.SetParent(_canvasRect, false);

            // Edit panel: - / + (resize selected), RESET, DONE. Hidden unless editing.
            _editPanel = new GameObject("TouchEditPanel");
            _editPanel.transform.SetParent(_canvasRect, false);
            var panelRect = _editPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -40f);
            panelRect.sizeDelta = new Vector2(430f, 50f);

            CreateTextButton("—", new Vector2(0.08f, 0.5f), new Vector2(70f, 44f),
                () => ResizeSelected(1f / SCALE_STEP), _editPanel.transform);
            CreateTextButton("+", new Vector2(0.28f, 0.5f), new Vector2(70f, 44f),
                () => ResizeSelected(SCALE_STEP), _editPanel.transform);
            CreateTextButton("RESET", new Vector2(0.55f, 0.5f), new Vector2(110f, 44f),
                ResetLayout, _editPanel.transform);
            CreateTextButton("DONE", new Vector2(0.85f, 0.5f), new Vector2(90f, 44f),
                () => SetEditMode(false), _editPanel.transform);

            _editPanel.SetActive(false);
        }

        private GameObject CreateTextButton(string label, Vector2 anchor, Vector2 size,
            UnityEngine.Events.UnityAction onClick, Transform parent = null)
        {
            var go = new GameObject("TouchButton_" + label);
            go.transform.SetParent(parent ?? _canvasRect, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.1f, 0.65f);
            var button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            var text = new GameObject("Label").AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(go.transform, false);
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 22;
            text.color = Color.white;
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            return go;
        }

        // ------------------------------------------------------------------
        // Layout (normalized -> canvas space, and back)
        // ------------------------------------------------------------------

        private void ApplyLayoutToButtons()
        {
            // Anchor-based: the button rect is fully determined by its
            // anchors, so it is correctly sized even when the canvas has not
            // laid out yet (its rect is 0x0 during Awake, which made the old
            // pixel-based version bail out and leave the buttons zero-sized
            // forever — the reason no touch controls showed in build #15).
            foreach (var button in _buttons)
            {
                var entry = _layout.GetButton(button.ButtonId);
                if (entry == null)
                    continue;

                var rect = button.RectTransform;
                float halfW = entry.Width * 0.5f;
                float halfH = entry.Height * 0.5f;
                rect.anchorMin = new Vector2(entry.CenterX - halfW, entry.CenterY - halfH);
                rect.anchorMax = new Vector2(entry.CenterX + halfW, entry.CenterY + halfH);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                button.RefreshVisual();
            }
        }

        private void SaveLayout()
        {
            if (_layout == null)
                return;

            // Anchors ARE the normalized layout now (see ApplyLayoutToButtons),
            // so saving reads them directly — no canvas size needed.
            foreach (var button in _buttons)
            {
                var entry = _layout.GetButton(button.ButtonId);
                if (entry == null)
                    continue;

                var rect = button.RectTransform;
                entry.CenterX = (rect.anchorMin.x + rect.anchorMax.x) * 0.5f;
                entry.CenterY = (rect.anchorMin.y + rect.anchorMax.y) * 0.5f;
                entry.Width = rect.anchorMax.x - rect.anchorMin.x;
                entry.Height = rect.anchorMax.y - rect.anchorMin.y;
            }

            _layout.Save();
        }

        private void ResetLayout()
        {
            _layout = TouchLayoutData.Default();
            ApplyLayoutToButtons();
            SelectButton(null);
        }

        private void ResizeSelected(float factor)
        {
            if (_selectedButton == null)
                return;

            var entry = _layout.GetButton(_selectedButton.ButtonId);
            if (entry == null)
                return;

            // Resize around the default size so repeated taps stay bounded.
            var defaults = TouchLayoutData.Default();
            var defaultEntry = defaults.GetButton(_selectedButton.ButtonId);
            float newWidth = Mathf.Clamp(entry.Width * factor,
                defaultEntry.Width * MIN_SCALE, defaultEntry.Width * MAX_SCALE);
            float newHeight = Mathf.Clamp(entry.Height * factor,
                defaultEntry.Height * MIN_SCALE, defaultEntry.Height * MAX_SCALE);
            entry.Width = newWidth;
            entry.Height = newHeight;
            ApplyLayoutToButtons();
        }

        // ------------------------------------------------------------------
        // Edit mode
        // ------------------------------------------------------------------

        private void SetEditMode(bool edit)
        {
            _editMode = edit;
            _editPanel.SetActive(edit);

            foreach (var button in _buttons)
            {
                button.EditMode = edit;
                button.RefreshVisual();
            }

            if (!edit)
            {
                SelectButton(null);
                SaveLayout();
            }
        }

        private void SelectButton(TouchFretButton button)
        {
            if (_selectedButton != null)
            {
                _selectedButton.Selected = false;
                _selectedButton.RefreshVisual();
            }

            _selectedButton = button;

            if (_selectedButton != null)
            {
                _selectedButton.Selected = true;
                _selectedButton.RefreshVisual();
            }
        }

        private void OnButtonTappedInEditMode(TouchFretButton button)
        {
            SelectButton(button);
        }

        private void OnButtonDragged(TouchFretButton button, Vector2 localPoint)
        {
            // localPoint is canvas-local pixels (origin at canvas center).
            // Convert it to a normalized anchor center; the canvas rect is
            // valid during a drag (layout has run by then).
            if (_canvasRect == null)
                return;

            float width = _canvasRect.rect.width;
            float height = _canvasRect.rect.height;
            if (width <= 0f || height <= 0f)
                return;

            float cx = localPoint.x / width + 0.5f;
            float cy = localPoint.y / height + 0.5f;

            var rect = button.RectTransform;
            float halfW = (rect.anchorMax.x - rect.anchorMin.x) * 0.5f;
            float halfH = (rect.anchorMax.y - rect.anchorMin.y) * 0.5f;
            cx = Mathf.Clamp(cx, halfW, 1f - halfW);
            cy = Mathf.Clamp(cy, halfH, 1f - halfH);
            rect.anchorMin = new Vector2(cx - halfW, cy - halfH);
            rect.anchorMax = new Vector2(cx + halfW, cy + halfH);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        // ------------------------------------------------------------------
        // Input injection (play mode)
        // ------------------------------------------------------------------

        private void OnButtonPressedChanged(TouchFretButton button, bool pressed)
        {
            if (_editMode)
                return;

            Inject(TouchButtonIds.ActionFor(button.ButtonId), pressed);
        }

        private void Inject(GuitarAction action, bool pressed)
        {
            var binding = GetBinding(action);
            if (binding == null)
                return;

            // Same clock the hardware bindings use; BasePlayer re-bases it with
            // the calibration offset. Never use Time.time here.
            binding.InjectInput(InputManager.CurrentInputTime, pressed);
        }

        private ControlBinding GetBinding(GuitarAction action)
        {
            string key = action.ToString();
            if (_bindings.TryGetValue(key, out var cached))
                return cached;

            var resolved = ResolveBinding(action);
            _bindings[key] = resolved;
            return resolved;
        }

        private ControlBinding ResolveBinding(GuitarAction action)
        {
            var gm = GameManager;
            if (gm == null)
                return null;

            // Single-player focus: first human, non-sitting-out five-fret player.
            foreach (BasePlayer basePlayer in gm.Players)
            {
                if (basePlayer == null || basePlayer.Player == null)
                    continue;

                var player = basePlayer.Player;
                if (player.Profile.GameMode != GameMode.FiveFretGuitar)
                    continue;
                if (player.Profile.IsBot || player.SittingOut || player.IsReplay)
                    continue;

                var bindings = player.Bindings[GameMode.FiveFretGuitar];
                return bindings.TryGetBindingByAction(action);
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Android first-run defaults (songs folder + low quality)
        // ------------------------------------------------------------------

        private void ApplyAndroidFirstRunDefaults()
        {
            if (Application.platform != RuntimePlatform.Android)
                return;

            try
            {
                string marker = Path.Combine(PathHelper.PersistentDataPath, "androidDefaultsApplied.txt");
                if (File.Exists(marker))
                    return;

                // Songs: <persistentDataPath>/songs (app-private, no storage
                // permission needed). The user copies song folders here, or
                // adds more folders in Settings > Songs.
                if (SettingsManager.Settings != null)
                {
                    string songsDir = Path.Combine(PathHelper.RealPersistentDataPath, "songs");
                    Directory.CreateDirectory(songsDir);
                    if (SettingsManager.Settings.SongFolders.Count == 0)
                        SettingsManager.Settings.SongFolders.Add(songsDir);

                    // Low quality by default on phones (Realme C25 class).
                    // SetValueWithoutNotify + ForceInvokeCallback applies the
                    // GraphicsManager callback without touching the settings menu.
                    SettingsManager.Settings.LowQuality.SetValueWithoutNotify(true);
                    SettingsManager.Settings.LowQuality.ForceInvokeCallback();
                    SettingsManager.SaveSettings();
                }

                Directory.CreateDirectory(PathHelper.PersistentDataPath);
                File.WriteAllText(marker, "1");
            }
            catch (System.Exception)
            {
                // Defaults are best-effort; a failure here must never break gameplay.
                // Fall back to the lowest quality level directly.
                try
                {
                    QualitySettings.SetQualityLevel(0, true);
                }
                catch (System.Exception)
                {
                }
            }
        }
    }
}
