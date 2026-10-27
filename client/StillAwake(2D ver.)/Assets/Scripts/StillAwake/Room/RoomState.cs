using System;
using System.Collections.Generic;

namespace StillAwake
{
    /// <summary>Состояние комнаты и героини за одну ночь — источник истины для сталкера и UI.</summary>
    public sealed class RoomState
    {
        public bool LightOn = true;
        public bool CurtainsClosed;
        public bool DoorLocked;
        public bool TableMoved;
        public bool FlashlightOn;
        public HideSpot Hidden = HideSpot.None;

        /// <summary>Шум 0–100. Растёт от действий, затухает со временем.</summary>
        public float Noise;

        /// <summary>Области, которые уже обысканы.</summary>
        public readonly HashSet<string> Looted = new HashSet<string>();

        /// <summary>Свет, шторы, дверь — всё, что меняет картинку.</summary>
        public event Action Changed;

        public void NotifyChanged() => Changed?.Invoke();
    }
}
