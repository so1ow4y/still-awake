using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StillAwake
{
    /// <summary>
    /// Кадр комнаты: переключает спрайты локаций (п.1 художника), строит области взаимодействия
    /// текущего кадра и отвечает за свет — тёмный кадр (п.3 художника) или затемнение, если его нет.
    /// </summary>
    public sealed class RoomView : MonoBehaviour
    {
        static readonly Color MissingArtColor = new Color(0.8f, 0.8f, 0.8f);
        static readonly Color CurtainsColor = new Color(0.45f, 0.36f, 0.33f, 0.97f);
        static readonly Color MoonlightColor = new Color(0.62f, 0.72f, 1f);

        public ViewId Current { get; private set; }
        public ViewDef CurrentDef => RoomContent.Get(Current);
        public IReadOnlyList<Hotspot> Hotspots => hotspots;
        public bool DebugHotspots { get; private set; }

        public event Action<ViewId> ViewChanged;

        readonly List<Hotspot> hotspots = new List<Hotspot>();
        SpriteRenderer frame;
        SpriteRenderer curtains;
        Transform hotspotRoot;
        Light2D globalLight;
        RoomState room;
        AuthoredRoom authored;
        float flicker = 1f;
        public bool IsAuthored => authored != null && Current == ViewId.Lounge;
        public StalkerSpot[] GetStalkerSpots(StalkerNode node) => IsAuthored ? authored.StalkerSpots(node) : RoomContent.StalkerSpots(node, Current);

        public void Init(RoomState state, Light2D light, Transform authoredRoot = null)
        {
            room = state;
            globalLight = light;
            if (authoredRoot != null) authored = new AuthoredRoom(authoredRoot);

            frame = CreateRenderer("Frame", 0);

            // Задёрнутые шторы — пока просто плашка поверх окна (над сталкером).
            curtains = CreateRenderer("Curtains (closed)", 15);
            curtains.sprite = ArtLibrary.White;
            curtains.color = CurtainsColor;
            Rect curtainsRect = authored != null ? authored.WindowRect : FrameSpace.ToWorld(RoomContent.CurtainsClosedArea);
            curtains.transform.localPosition = curtainsRect.center;
            curtains.transform.localScale = new Vector3(curtainsRect.width, curtainsRect.height, 1f);

            hotspotRoot = new GameObject("Hotspots").transform;
            hotspotRoot.SetParent(transform, false);

            room.Changed += RefreshFrame;
        }

        public void Show(ViewId id)
        {
            Current = id == ViewId.Lounge && authored == null ? ViewId.Window : id;
            authored?.SetVisible(IsAuthored);
            RefreshFrame();
            RebuildHotspots();
            ViewChanged?.Invoke(Current);
        }

        /// <summary>Мигание лампы: яркость включённого света (1 — обычная). Свет в комнате при этом не выключается.</summary>
        public void SetFlicker(float brightness)
        {
            flicker = brightness;
            RefreshFrame();
        }

        public void SetDebugHotspots(bool visible)
        {
            DebugHotspots = visible;
            foreach (Hotspot hotspot in hotspots) hotspot.SetDebugVisible(visible);
        }

        public void RefreshFrame()
        {
            Rect curtainsRect = IsAuthored ? authored.WindowRect : FrameSpace.ToWorld(RoomContent.CurtainsClosedArea);
            curtains.transform.localPosition = curtainsRect.center;
            curtains.transform.localScale = new Vector3(curtainsRect.width, curtainsRect.height, 1f);
            frame.enabled = !IsAuthored;
            if (IsAuthored)
            {
                frame.enabled = false;
                curtains.enabled = room.CurtainsClosed;
                authored.SetLight(room.LightOn, room.FlashlightOn, flicker);
                SetLight(1f, Color.white);
                return;
            }
            ViewDef def = CurrentDef;

            Sprite sprite = null;
            bool darkArt = false;
            if (!room.LightOn && !string.IsNullOrEmpty(def.DarkSprite))
            {
                sprite = ArtLibrary.Load(def.DarkSprite);
                darkArt = sprite != null;
            }
            if (sprite == null) sprite = ArtLibrary.Load(def.Sprite);

            frame.color = sprite != null ? Color.white : MissingArtColor;
            if (sprite == null) sprite = ArtLibrary.White;

            // Любой арт растягивается ровно на кадр 30×20 юнитов, даже если художник выдал другое разрешение.
            frame.sprite = sprite;
            Vector2 size = sprite.bounds.size;
            frame.transform.localScale = new Vector3(FrameSpace.WorldSize.x / size.x, FrameSpace.WorldSize.y / size.y, 1f);

            curtains.enabled = Current == ViewId.Window && room.CurtainsClosed;

            if (room.LightOn) SetLight(flicker, Color.white);
            else if (darkArt) SetLight(room.FlashlightOn ? 1.2f : 1f, Color.white);
            else SetLight(room.FlashlightOn ? 0.65f : 0.3f, MoonlightColor);
        }

        void SetLight(float intensity, Color color)
        {
            if (globalLight == null) return;
            globalLight.intensity = intensity;
            globalLight.color = color;
        }

        void RebuildHotspots()
        {
            foreach (Hotspot hotspot in hotspots) Destroy(hotspot.gameObject);
            hotspots.Clear();

            if (IsAuthored)
            {
                authored.BuildHotspots(hotspots, hotspotRoot);
                foreach (var hotspot in hotspots) hotspot.SetDebugVisible(DebugHotspots);
                return;
            }

            foreach (HotspotDef def in CurrentDef.Hotspots)
            {
                Hotspot hotspot = Hotspot.Create(def, hotspotRoot);
                hotspot.SetDebugVisible(DebugHotspots);
                hotspots.Add(hotspot);
            }
        }

        void OnDestroy()
        {
            if (room != null) room.Changed -= RefreshFrame;
            authored?.RestoreColors();
            authored?.SetVisible(true);
        }

        SpriteRenderer CreateRenderer(string objectName, int sortingOrder)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sortingOrder = sortingOrder;
            return spriteRenderer;
        }
    }
}
