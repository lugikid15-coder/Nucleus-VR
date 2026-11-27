using NucleusVR.Core;
using NucleusVR.Gameplay;
using NucleusVR.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NucleusVR.UI
{
    internal sealed class CalibrationHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color32(13, 22, 34, 255);
        private static readonly Color Foreground = new Color32(238, 244, 243, 255);
        private static readonly Color Muted = new Color32(163, 186, 190, 255);
        private static readonly Color Mint = new Color32(146, 237, 197, 255);
        private DesktopRigDriver desktop;
        private CalibrationInteraction calibration;
        private OnlineSession session;
        private GameObject overlay;
        private GameObject menu;
        private GameObject reticle;
        private Text status;
        private Text sessionStatus;
        private Text turnButtonText;
        private Text roomCode;
        private Text leaveLabel;
        private InputField codeInput;
        private Button quickJoin;
        private Button createRoom;
        private Button joinRoom;
        private Button copyCode;
        private Button leave;
        private Text copyLabel;
        private string displayedCode;
        private float copiedUntil;
        private Font font;
        private GameObject inputEvents;

        private GameFlow gameplay;
        private Text modeTitle;

        public void Build(DesktopRigDriver driver, CalibrationInteraction interaction, OnlineSession network,
            GameFlow flow)
        {
            gameplay = flow;
            desktop = driver;
            calibration = interaction;
            session = network;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            inputEvents = new GameObject("Desktop UI input", typeof(EventSystem));
            inputEvents.transform.SetParent(transform, false);
            inputEvents.AddComponent<InputSystemUIInputModule>();

            overlay = new GameObject("Calibration HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            overlay.transform.SetParent(transform, false);
            Canvas canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = overlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440f, 900f);
            scaler.matchWidthOrHeight = 0.4f;

            var ribbon = Panel(overlay.transform, "Status ribbon", new Color32(13, 22, 34, 231));
            Place(ribbon, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(25f, -25f), new Vector2(418f, 106f));
            modeTitle = Label(ribbon, "", 16, Mint, 17f, 12f, 382f, 24f);
            status = Label(ribbon, "", 16, Foreground, 17f, 36f, 382f, 64f);

            var pointer = Panel(overlay.transform, "Reticle", new Color32(227, 248, 240, 175));
            Place(pointer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
            reticle = pointer.gameObject;

            var panel = Panel(overlay.transform, "Control panel", Ink, true);
            menu = panel.gameObject;
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 592f));
            var accent = Panel(panel, "Accent", Mint);
            TopLeft(accent, 0f, 0f, 620f, 3f);
            Label(panel, "NUCLEUS RESEARCH FACILITY", 15, Mint, 33f, 26f, 554f, 22f);
            Label(panel, "Hand calibration", 42, Foreground, 31f, 53f, 554f, 57f);
            Label(panel, "Hold either mouse button and drag to move that hand.\nScroll to reach farther. Push off walls or the floor to move.", 19,
                Foreground, 33f, 122f, 552f, 73f);
            Label(panel, "Mouse to look   ·   Q / E to turn   ·   Ctrl to crouch\nOffline modes 1–4   ·   F1 or Esc for this panel", 18,
                Muted, 33f, 202f, 552f, 54f);
            Button(panel, "Enter calibration", 33f, 273f, 330f, () => desktop.SetMenu(false), true);
            Button(panel, "Snap turn", 378f, 273f, 208f, calibration.ToggleTurning, false, out turnButtonText);

            var divider = Panel(panel, "Divider", new Color32(49, 66, 78, 255));
            TopLeft(divider, 33f, 336f, 553f, 1f);
            Label(panel, "ONLINE MULTIPLAYER", 15, Mint, 33f, 351f, 553f, 23f);
            sessionStatus = Label(panel, "", 17, Muted, 33f, 533f, 553f, 41f);
            if (session == null)
            {
                sessionStatus.text = "Generate the direct-player prefab from Nucleus VR › Configure Project.";
                return;
            }

            quickJoin = Button(panel, "Quick Join", 33f, 384f, 181f, session.QuickJoin, true);
            createRoom = Button(panel, "Create Private", 226f, 384f, 214f, session.CreatePrivate, false);
            leave = Button(panel, "Leave", 452f, 384f, 134f, session.Leave, false, out leaveLabel);

            codeInput = CodeField(panel);
            joinRoom = Button(panel, "Join by Code", 257f, 439f, 183f, () => session.JoinCode(codeInput.text), false);
            copyCode = Button(panel, "Copy Code", 452f, 439f, 134f, CopyRoomCode, false, out copyLabel);
            roomCode = Label(panel, "", 20, Mint, 33f, 496f, 553f, 29f);
            RefreshOnlineControls();
        }

        private void LateUpdate()
        {
            if (overlay == null)
                return;

            bool show = desktop.IsDesktop;
            overlay.SetActive(show);
            inputEvents.SetActive(show);
            menu.SetActive(desktop.MenuOpen);
            reticle.SetActive(!desktop.MenuOpen);
            modeTitle.text = gameplay.ModeTitle;
            status.text = gameplay.LocalStatus;
            turnButtonText.text = calibration.SmoothTurn ? "Smooth turn" : "Snap turn";
            if (session != null && menu.activeSelf)
                RefreshOnlineControls();
        }

        private void RefreshOnlineControls()
        {
            bool canStart = !session.Busy && !session.InRoom;
            bool hasCode = session.InRoom && !string.IsNullOrEmpty(session.RoomCode);
            quickJoin.interactable = canStart;
            createRoom.interactable = canStart;
            joinRoom.interactable = canStart && codeInput.text.Length == OnlineRoomCode.Length;
            codeInput.interactable = canStart;
            copyCode.interactable = hasCode;
            leave.interactable = session.Busy || session.InRoom;
            leaveLabel.text = session.Busy ? "Cancel" : "Leave";
            copyLabel.text = hasCode && Time.unscaledTime < copiedUntil ? "Copied" : "Copy Code";
            string code = hasCode ? session.RoomCode : "";
            if (displayedCode != code)
            {
                displayedCode = code;
                roomCode.text = hasCode ? $"Room code  {code}" : "";
                copiedUntil = 0f;
            }
            sessionStatus.text = session.Status;
        }

        private void CopyRoomCode()
        {
            if (session.InRoom && !string.IsNullOrEmpty(session.RoomCode))
            {
                GUIUtility.systemCopyBuffer = session.RoomCode;
                copiedUntil = Time.unscaledTime + 1.6f;
            }
        }

        private InputField CodeField(Transform parent)
        {
            var field = Panel(parent, "Room code", new Color32(27, 43, 58, 255), true);
            TopLeft(field, 33f, 439f, 212f, 43f);
            InputField input = field.gameObject.AddComponent<InputField>();
            input.textComponent = Label(field, "", 18, Foreground, 13f, 3f, 182f, 37f);
            input.placeholder = Label(field, "6-character room code", 18, Muted, 13f, 3f, 182f, 37f);
            input.contentType = InputField.ContentType.Alphanumeric;
            input.characterLimit = OnlineRoomCode.Length;
            input.lineType = InputField.LineType.SingleLine;
            input.targetGraphic = field.GetComponent<Image>();
            return input;
        }

        private Text Label(Transform parent, string copy, int size, Color tone, float x, float y, float w, float h)
        {
            var element = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rect = element.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            TopLeft(rect, x, y, w, h);
            var label = element.GetComponent<Text>();
            label.font = font;
            label.text = copy;
            label.fontSize = size;
            label.color = tone;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return label;
        }

        private Button Button(Transform parent, string title, float x, float y, float w,
            UnityEngine.Events.UnityAction action, bool primary) =>
            Button(parent, title, x, y, w, action, primary, out _);

        private Button Button(Transform parent, string title, float x, float y, float w,
            UnityEngine.Events.UnityAction action, bool primary, out Text text)
        {
            var rect = Panel(parent, title, primary ? Mint : new Color32(42, 63, 79, 255), true);
            TopLeft(rect, x, y, w, 43f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(action);
            text = Label(rect, title, 19, primary ? Ink : Foreground, 0f, 0f, w, 43f);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private static RectTransform Panel(Transform parent, string label, Color color, bool interactive = false)
        {
            var element = new GameObject(label, typeof(RectTransform), typeof(Image));
            var rect = element.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Image image = element.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = interactive;
            return rect;
        }

        private static void TopLeft(RectTransform rect, float x, float y, float width, float height) =>
            Place(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height));

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
