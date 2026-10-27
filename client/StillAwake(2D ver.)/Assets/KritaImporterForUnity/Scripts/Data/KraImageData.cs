using UnityEngine;
using System.Collections.Generic;

namespace KraImporter.Data
{
    [System.Serializable]
    public class KraImageData
    {
        public readonly int Width, Height;

        public readonly string Name;

        public readonly Color BackgroundSimpleColor;

        public List<KraLayerData> Layers = new(); 

        public Texture2D MergedImage;

        public KraImageData(int width, int height, string name, Color backgroundSimpleColor, Texture2D mergedImage)
        {
            Width = width;
            Height = height;
            Name = name;
            BackgroundSimpleColor = backgroundSimpleColor;
            MergedImage = mergedImage;
        }
    }
}
