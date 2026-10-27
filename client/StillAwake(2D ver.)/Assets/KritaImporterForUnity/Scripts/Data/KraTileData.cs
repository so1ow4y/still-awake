using UnityEngine;

namespace KraImporter.Data
{
    [System.Serializable]
    public class KraTileData
    {
        public int TileIndex;

        public int X, Y;

        public string Type;

        public int Size;

        public Color32[] TilePixels;

        public int Flag;
    }
}
