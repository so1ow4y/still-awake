using UnityEngine;
using System.Collections.Generic;

namespace KraImporter.Data
{
    [System.Serializable]
    public class KraLayerData
    {
        public enum NodeTypeEnum
        {
            paintlayer,
            grouplayer,
            transparencymask
        }

        // == xml content ==

        public byte Opacity { get; private set; }

        public string FileName { get; private set; }

        public string LayerName { get; private set; }

        public string Uuid { get; private set; }

        public NodeTypeEnum NodeType { get; private set; }

        // === Other data ===

        public List<string> LayersIdsInGroup = new();

        public List<string> MasksIds = new();

        public int Depth { get; set; }

        // == file content ==

        public int Version { get; private set; }

        public int TileWidth { get; private set; }

        public int TileHeight { get; private set; }

        public int PixelSize { get; private set; }

        public int TileCount { get; private set; }

        public Color32[] LayerPixels;

        public KraTileData[] Tiles;

        public void SetLayerXmlData(string fileName, string layerName, byte opacity, string uuid, NodeTypeEnum nodeType, int depth)
        {
            FileName = fileName;
            LayerName = layerName;
            Opacity = opacity;
            Uuid = uuid;
            NodeType = nodeType;
            Depth = depth;
        }

        public void SetLayerFileData(int version, int tileWidth, int tileHeight, int pixelSize, int tileCount)
        {
            Version = version;
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            PixelSize = pixelSize;
            TileCount = tileCount;
        }
    }
}
