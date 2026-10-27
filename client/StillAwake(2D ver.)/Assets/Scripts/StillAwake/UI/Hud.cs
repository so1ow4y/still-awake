using System;
using UnityEngine;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>
    /// Постоянный интерфейс: часы, подсказка, кнопки поворота головы, оверлей укрытия,
    /// подсказка у курсора, всплывающие сообщения и затемнение при смене кадра.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        // Щель, через которую видно комнату из укрытия (доли экрана).
        const float WardrobeSlitMin = 0.42f;
        const float WardrobeSlitMax = 0.58f;
        const float BedSlitMin = 0.35f;
        const float BedSlitMax = 0.6f;

        static readonly Color HintColor = new Color(1f, 1f, 1f, 0.85f);
        static readonly Color HiddenShade = new Color(0f, 0f, 0f, 0.93f);
        static readonly Color TurnButtonColor = new Color(0f, 0f, 0f, 0.35f);

        public event Action ReturnClicked;
        public event Action TurnLeftClicked;
        public event Action TurnRightClicked;
        public event Action UnhideClicked;
        public event Action PeepholeCloseClicked;

        /// <summary>Открыт вид в глазок.</summary>
        public bool PeepholeOpen => peepholeRoot.gameObject.activeSelf;

        /// <summary>
        /// Вид «изнутри» (глазок, укрытие) и где на нём появляется сталкер: доли экрана от левого нижнего
        /// угла — совпадает с красным пунктиром на схемах Views/*.png. Wander — насколько ходит влево-вправо.
        /// </summary>
        sealed class CloseUp
        {
            public Image Background, Stalker;
            public Vector2 Min, Max;
            public float Wander;
            public bool HasArt;
        }

        bool singleRoom;
        Text help, lockText;
        Text clock, hint, viewTitle, debugText, hiddenText, toastText, tooltipText;
        Button turnLeft, turnRight, returnButton, unhideButton;
        ViewId currentView;
        RectTransform hiddenRoot, wardrobeShade, bedShade, toast, tooltip, peepholeRoot;
        CloseUp wardrobeView, bedView, peepholeView;
        Text peepholeText;
        Button peepholeClose;
        Image fade;
        float toastTimer, fadeAlpha, fadeSpeed;

        public static Hud Create(RectTransform canvas)
        {
            RectTransform root = Ui.Stretch(Ui.Node("HUD", canvas));
            var hud = root.gameObject.AddComponent<Hud>();
            hud.BuildBase(root);
            hud.RefreshKeyHints();
            KeyBindings.Changed += hud.RefreshKeyHints;
            return hud;
        }

        void OnDestroy() => KeyBindings.Changed -= RefreshKeyHints;

        /// <summary>Подсказки и подписи кнопок — по текущим клавишам из настроек.</summary>
        void RefreshKeyHints()
        {
            string Key(GameAction action) => KeyBindings.Short(action);
            string tail = Key(GameAction.Phone) + " — телефон · " + Key(GameAction.PhoneHide) + " или " +
                          Key(GameAction.PhoneHideOnHover) + " по нему — спрятать, " + Key(GameAction.PhonePeekOnHover) + " — опустить · " +
                          Key(GameAction.CameraLock) + " — закрепить камеру · Esc — пауза";
            help.text = singleRoom
                ? "Колесо — приблизить · " + Key(GameAction.ResetZoom) + " — общий вид · ЛКМ — действие\n" + tail
                : "ЛКМ по предмету — действия · " + Key(GameAction.TurnLeft) + "/" + Key(GameAction.TurnRight) + " — повернуться · " +
                  Key(GameAction.Sofa) + " — на диван · " + Key(GameAction.Window) + " — к окну · предметы снизу можно перетаскивать на объекты\n" + tail;
            Ui.SetLabel(returnButton, "На диван [" + Key(GameAction.Sofa) + "]");
            Ui.SetLabel(unhideButton, "Вылезти [" + Key(GameAction.Unhide) + "]");
            Ui.SetLabel(peepholeClose, "Отойти от двери [" + Key(GameAction.Unhide) + "]");
            lockText.text = "Камера закреплена [" + Key(GameAction.CameraLock) + "]";
        }

        public void SetCameraLocked(bool locked) => lockText.gameObject.SetActive(locked);

        /// <summary>Затемнить экран (переход между кадрами); FlashFromBlack потом проявляет новый кадр.</summary>
        public System.Collections.IEnumerator FadeOut(float seconds)
        {
            fadeSpeed = 0f;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
            {
                fadeAlpha = Mathf.Max(fadeAlpha, t);
                fade.color = new Color(0f, 0f, 0f, fadeAlpha);
                yield return null;
            }
            fadeAlpha = 1f;
            fade.color = Color.black;
        }

        /// <summary>Подсказка, тосты и затемнение — поверх всех окон, поэтому создаются последними.</summary>
        public void CreateOverlay(RectTransform canvas)
        {
            RectTransform root = Ui.Stretch(Ui.Node("HUD Overlay", canvas));

            // Внизу — мысли героини (субтитры), поэтому события («Стук в дверь») — сверху, под подсказкой.
            toastText = Ui.AutoSizeBox("Toast", root, new Color(0f, 0f, 0f, 0.78f), 26, out toast);
            Ui.Place(toast, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -124f), Vector2.zero);
            toast.gameObject.SetActive(false);

            tooltipText = Ui.AutoSizeBox("Tooltip", root, new Color(0f, 0f, 0f, 0.8f), 22, out tooltip);
            tooltip.pivot = new Vector2(0f, 1f);
            tooltip.gameObject.SetActive(false);

            fade = Ui.Image("Fade", root, new Color(0f, 0f, 0f, 0f));
            Ui.Stretch(fade.rectTransform);
        }

        void BuildBase(RectTransform root)
        {
            BuildHiddenOverlay(root);

            Image topBar = Ui.Image("Top bar", root, new Color(0f, 0f, 0f, 0.45f));
            Ui.Anchors(topBar.rectTransform, 0f, 1f, 1f, 1f);
            topBar.rectTransform.pivot = new Vector2(0.5f, 1f);
            topBar.rectTransform.sizeDelta = new Vector2(0f, 112f);

            clock = Ui.Text("Clock", root, "12 AM", 44, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            Ui.Place(clock.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(400f, 56f));

            hint = Ui.Text("Hint", root, "", 24, HintColor, TextAnchor.UpperCenter);
            Ui.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(760f, 40f));

            viewTitle = Ui.Text("View title", root, "", 28, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            Ui.Place(viewTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -14f), new Vector2(400f, 40f));

            help = Ui.Text("Controls", root, "", 16, new Color(1f, 1f, 1f, 0.6f), TextAnchor.UpperLeft);
            Ui.Place(help.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -54f), new Vector2(560f, 60f));

            lockText = Ui.Text("Camera locked", root, "", 18, new Color(1f, 0.85f, 0.45f), TextAnchor.UpperRight, FontStyle.Bold);
            Ui.Place(lockText.rectTransform, Vector2.one, Vector2.one, new Vector2(-28f, -18f), new Vector2(420f, 30f));
            lockText.gameObject.SetActive(false);

            turnLeft = Ui.Button("Turn left", root, "←", () => TurnLeftClicked?.Invoke(), 22, TurnButtonColor);
            Ui.Place((RectTransform)turnLeft.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(120f, 220f));

            turnRight = Ui.Button("Turn right", root, "→", () => TurnRightClicked?.Invoke(), 22, TurnButtonColor);
            Ui.Place((RectTransform)turnRight.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(120f, 220f));

            returnButton = Ui.Button("Return to sofa", root, "На диван [S]", () => ReturnClicked?.Invoke(), 20);
            Ui.Place((RectTransform)returnButton.transform, Vector2.zero, Vector2.zero, new Vector2(24f, 24f), new Vector2(220f, 56f));

            debugText = Ui.Text("Debug", root, "", 18, new Color(1f, 0.9f, 0.3f), TextAnchor.LowerLeft);
            Ui.Place(debugText.rectTransform, Vector2.zero, Vector2.zero, new Vector2(24f, 100f), new Vector2(720f, 220f));
            debugText.gameObject.SetActive(false);
        }

        void BuildHiddenOverlay(RectTransform root)
        {
            hiddenRoot = Ui.Stretch(Ui.Node("Hidden overlay", root));

            // Вид из укрытия (Views/*.png). Если картинки нет — тёмные плашки со щелью, как раньше.
            wardrobeView = BuildCloseUp("Wardrobe view", hiddenRoot, ArtPaths.HideWardrobe, new Vector2(0.44f, 0.15f), new Vector2(0.56f, 0.8f), 0.015f);
            bedView = BuildCloseUp("Under bed view", hiddenRoot, ArtPaths.HideUnderBed, new Vector2(0.19f, 0.4f), new Vector2(0.31f, 0.7f), 0.19f);

            wardrobeShade = Ui.Stretch(Ui.Node("Wardrobe shade", hiddenRoot));
            Ui.Anchors(Ui.Image("Left", wardrobeShade, HiddenShade).rectTransform, 0f, 0f, WardrobeSlitMin, 1f);
            Ui.Anchors(Ui.Image("Right", wardrobeShade, HiddenShade).rectTransform, WardrobeSlitMax, 0f, 1f, 1f);

            bedShade = Ui.Stretch(Ui.Node("Bed shade", hiddenRoot));
            Ui.Anchors(Ui.Image("Bottom", bedShade, HiddenShade).rectTransform, 0f, 0f, 1f, BedSlitMin);
            Ui.Anchors(Ui.Image("Top", bedShade, HiddenShade).rectTransform, 0f, BedSlitMax, 1f, 1f);

            // Подпись и кнопка — выше хотбара инвентаря, который занимает низ экрана.
            hiddenText = Ui.Text("Hidden text", hiddenRoot, "", 26, Color.white);
            Ui.Place(hiddenText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 210f), new Vector2(1000f, 40f));

            unhideButton = Ui.Button("Unhide", hiddenRoot, "Вылезти", () => UnhideClicked?.Invoke(), 24);
            Ui.Place((RectTransform)unhideButton.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 130f), new Vector2(300f, 64f));

            hiddenRoot.gameObject.SetActive(false);

            peepholeRoot = Ui.Stretch(Ui.Node("Peephole", root));
            peepholeView = BuildCloseUp("Peephole view", peepholeRoot, ArtPaths.Peephole, new Vector2(0.38f, 0.05f), new Vector2(0.62f, 0.8f), 0f);
            if (!peepholeView.HasArt) peepholeView.Background.color = new Color(0.12f, 0.12f, 0.1f);
            peepholeText = Ui.Text("Peephole text", peepholeRoot, "", 26, Color.white);
            Ui.Place(peepholeText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 210f), new Vector2(1000f, 40f));
            peepholeClose = Ui.Button("Leave peephole", peepholeRoot, "Отойти от двери", () => PeepholeCloseClicked?.Invoke(), 24);
            Ui.Place((RectTransform)peepholeClose.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 130f), new Vector2(340f, 64f));
            peepholeRoot.gameObject.SetActive(false);
        }

        static CloseUp BuildCloseUp(string name, RectTransform parent, string artPath, Vector2 min, Vector2 max, float wander)
        {
            Sprite art = ArtLibrary.Load(artPath);
            var view = new CloseUp { Min = min, Max = max, Wander = wander, HasArt = art != null };
            view.Background = Ui.Image(name, parent, Color.white, art, raycast: true);
            Ui.Stretch(view.Background.rectTransform);
            Sprite stalkerArt = ArtLibrary.Load(ArtPaths.Stalker);
            view.Stalker = Ui.Image("Stalker", view.Background.transform, stalkerArt != null ? Color.white : new Color(0.75f, 0.05f, 0.05f), stalkerArt);
            view.Stalker.preserveAspect = stalkerArt != null;
            Ui.Anchors(view.Stalker.rectTransform, min.x, min.y, max.x, max.y);
            view.Stalker.gameObject.SetActive(false);
            view.Background.gameObject.SetActive(false);
            return view;
        }

        /// <summary>Вид в глазок: коридор, а если сталкер у двери — он сам.</summary>
        public void ShowPeephole(bool open, string text = "")
        {
            peepholeRoot.gameObject.SetActive(open);
            peepholeView.Background.gameObject.SetActive(open);
            peepholeText.text = text;
        }

        /// <summary>Сталкер на открытом виде «изнутри»: у двери — в глазке, в комнате — из укрытия. Каждый кадр.</summary>
        public void SetCloseUpStalker(bool atDoor, bool inRoom)
        {
            UpdateCloseUp(peepholeView, atDoor);
            UpdateCloseUp(wardrobeView, inRoom);
            UpdateCloseUp(bedView, inRoom);
        }

        static void UpdateCloseUp(CloseUp view, bool visible)
        {
            bool show = visible && view.Background.gameObject.activeInHierarchy;
            if (view.Stalker.gameObject.activeSelf != show) view.Stalker.gameObject.SetActive(show);
            if (!show || view.Wander <= 0f) return;
            // Ходит от левого края зоны к правому и обратно.
            float shift = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(Time.time / 3.5f, 1f)) * 2f * view.Wander;
            Ui.Anchors(view.Stalker.rectTransform, view.Min.x + shift, view.Min.y, view.Max.x + shift, view.Max.y);
        }

        public void SetClock(string text) => clock.text = text;

        public void SetHint(string text, bool alarm)
        {
            hint.text = text;
            hint.color = alarm ? Ui.Accent : HintColor;
        }

        public void SetView(ViewDef def)
        {
            currentView = def.Id;
            viewTitle.text = def.Title;
            returnButton.gameObject.SetActive(currentView != ViewId.Lounge && turnLeft.gameObject.activeSelf);
            Ui.SetLabel(turnLeft, "←\n" + RoomContent.Get(def.Left).Title);
            Ui.SetLabel(turnRight, "→\n" + RoomContent.Get(def.Right).Title);
        }

        public void SetSingleRoom(bool value)
        {
            singleRoom = value;
            RefreshKeyHints();
        }

        public void SetTurnVisible(bool visible)
        {
            turnLeft.gameObject.SetActive(visible && !singleRoom);
            turnRight.gameObject.SetActive(visible && !singleRoom);
            returnButton.gameObject.SetActive(visible && currentView != ViewId.Lounge);
        }

        public void ShowHidden(HideSpot spot)
        {
            hiddenRoot.gameObject.SetActive(spot != HideSpot.None);
            wardrobeView.Background.gameObject.SetActive(spot == HideSpot.Wardrobe && wardrobeView.HasArt);
            bedView.Background.gameObject.SetActive(spot == HideSpot.UnderBed && bedView.HasArt);
            wardrobeShade.gameObject.SetActive(spot == HideSpot.Wardrobe && !wardrobeView.HasArt);
            bedShade.gameObject.SetActive(spot == HideSpot.UnderBed && !bedView.HasArt);
            hiddenText.text = spot == HideSpot.Wardrobe
                ? "Ты в шкафу. Сквозь щель видно комнату."
                : "Ты под кроватью. Видно только пол.";
        }

        public void ShowToast(string text, float seconds = 2.5f)
        {
            toastText.text = text;
            toast.gameObject.SetActive(true);
            toastTimer = seconds;
        }

        public void SetTooltip(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            if (tooltip.gameObject.activeSelf != show) tooltip.gameObject.SetActive(show);
            if (!show) return;
            tooltipText.text = text;
            // У overlay-канваса позиция RectTransform — это экранные пиксели.
            tooltip.position = GameInput.PointerPosition + new Vector2(18f, -18f);
        }

        public void FlashFromBlack(float seconds = 0.25f)
        {
            fadeAlpha = 1f;
            fadeSpeed = 1f / seconds;
        }

        /// <summary>null — спрятать отладочный текст.</summary>
        public void SetDebug(string text)
        {
            debugText.gameObject.SetActive(text != null);
            if (text != null) debugText.text = text;
        }

        // Интерфейс живёт по реальному времени: на паузе (timeScale = 0) затемнение не должно залипать.
        void Update()
        {
            if (toastTimer > 0f)
            {
                toastTimer -= Time.unscaledDeltaTime;
                if (toastTimer <= 0f) toast.gameObject.SetActive(false);
            }

            if (fadeAlpha > 0f)
            {
                fadeAlpha = Mathf.Max(0f, fadeAlpha - fadeSpeed * Time.unscaledDeltaTime);
                fade.color = new Color(0f, 0f, 0f, fadeAlpha);
            }
        }
    }
}
