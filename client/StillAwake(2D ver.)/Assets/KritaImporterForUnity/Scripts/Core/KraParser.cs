using KraImporter.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace KraImporter.Core
{
    public class KraParser
    {
        public KraImageData ImageDataResoult;
        readonly Dictionary<string, Color32[]> _processedLayerPixels = new();

        /// <summary>
        /// Main parser entry point. Opens a Krita file from a full system path and extracts document metadata, layer metadata, layer pixel data, and the merged preview image.
        /// </summary>
        /// <param name="fullFilePath">full path starts with f.ex C:\\</param>
        /// <returns>A KraImageData instance containing the image width, height, name, background color, layer array and merged image.</returns>
        public KraImageData ParseKraFile(string fullFilePath)
        {
            // Placeholder for parsing logic
            if (!File.Exists(fullFilePath))
            {
                Debug.LogError("File not found.");
                return null;
            }
            
            using var archive = ZipFile.OpenRead(fullFilePath);
            Texture2D mergedImage = default;

            foreach (var entry in archive.Entries)
            {
                // order of entries: maindoc => layers => mergedimage
                if (entry.Name.Equals("maindoc.xml"))
                    TransferXmlToData(entry, mergedImage);

                if (ImageDataResoult != null && entry.FullName.StartsWith($"{ImageDataResoult.Name}/layers/") &&
                    ImageDataResoult.Layers.Any(layer => layer.FileName == entry.Name)) // layers and grup layers
                    TransferFileToData(entry);

                if (entry.Name.Equals("mergedimage.png"))
                    TransferMargedImageToData(entry, fullFilePath);
            }

            // Apply masks, group layers
            PostProcessingPixels();

            return ImageDataResoult;
        }

        void PostProcessingPixels()
        {
            _processedLayerPixels.Clear();
            foreach (var layer in ImageDataResoult.Layers)
                layer.LayerPixels = PostProceedLayer(layer);
        }

        Color32[] PostProceedLayer(KraLayerData layer)
        {
            if (layer == null)
                return null;

            if (_processedLayerPixels.TryGetValue(layer.Uuid, out Color32[] cachedPixels))
                return cachedPixels;

            int width = ImageDataResoult.Width;
            int height = ImageDataResoult.Height;

            Color32[] resultPixels;

            if (layer.NodeType == KraLayerData.NodeTypeEnum.grouplayer)
            {
                List<Color32[]> layersPixels = new();

                foreach (string layerId in layer.LayersIdsInGroup)
                {
                    KraLayerData layerFound = ImageDataResoult.Layers.Find(l => l.Uuid == layerId);

                    if (layerFound == null)
                        continue;

                    if (layerFound.NodeType == KraLayerData.NodeTypeEnum.transparencymask)
                        continue;

                    Color32[] childPixels = PostProceedLayer(layerFound);

                    if (childPixels != null)
                        layersPixels.Add(childPixels);
                }

                Color32[] combinedLayerPixels = KraTextureUtils.MergeLayerPixels(width, height, layersPixels);

                resultPixels = TryProceedMaskToLayer(layer, combinedLayerPixels);
            }
            else
                resultPixels = TryProceedMaskToLayer(layer, layer.LayerPixels);

            if (!string.IsNullOrEmpty(layer.Uuid))
                _processedLayerPixels[layer.Uuid] = resultPixels;

            return resultPixels;
        }

        Color32[] TryProceedMaskToLayer(KraLayerData layer, Color32[] layerPixelsRef)
        {
            Color32[] layerPixels = layerPixelsRef.ToArray();

            if (layer.MasksIds == null || layer.MasksIds.Count == 0)
                return layerPixels;

            foreach (var maskId in layer.MasksIds)
            {
                KraLayerData maskLayerFound = ImageDataResoult.Layers.Find(l => l.Uuid == maskId);
                if (maskLayerFound == null)
                    continue;

                layerPixels = KraTextureUtils.ApplyMaskToPixels(
                    layerPixels,
                    maskLayerFound.LayerPixels
                );
            }

            return layerPixels;
        }

        void TransferMargedImageToData(ZipArchiveEntry entry, string fullFilePath)
        {
            using var stream = entry.Open();
            byte[] buffer = new byte[entry.Length];
            stream.Read(buffer, 0, (int)entry.Length);

            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false);
            texture.LoadImage(buffer);
            ImageDataResoult.MergedImage = texture;
            ImageDataResoult.MergedImage.name = Path.GetFileNameWithoutExtension(fullFilePath);
        }

        void TransferXmlToData(ZipArchiveEntry entry, Texture2D mergedImage)
        {
            XDocument doc = XDocument.Load(entry.Open());
            XNamespace ns = doc.Root.Name.Namespace;

            // Example of an GlobalAssistantsColor element in the XML:
            /*
            * < GlobalAssistantsColor SimpleColorData = "176,176,176,255" />
            */

            var globalAssistantsColor = doc.Descendants(ns + "GlobalAssistantsColor").ElementAt(0);
            var val = globalAssistantsColor.Attribute("SimpleColorData");
            var colorValues = val.Value.Split(',');
            Color32 backgroundSimpleColor = new(
                byte.Parse(colorValues[0].Trim()),
                byte.Parse(colorValues[1].Trim()),
                byte.Parse(colorValues[2].Trim()),
                byte.Parse(colorValues[3].Trim())
            );

            // Example of an IMAGE element in the XML:
            /*
            * <IMAGE colorspacename = "RGBA" width = "512" y - res = "100" name = "unnamed" x - res = "100" 
            * mime = "application/x-kra" description = "" profile = "sRGB built-in" height = "512"> <layers> </IMAGE>
            */

            var image = doc.Descendants(ns + "IMAGE").ElementAt(0);
            var imageWidth = int.Parse(image.Attribute("width")?.Value);
            var imageHeight = int.Parse(image.Attribute("height")?.Value);
            var imageName = image.Attribute("name")?.Value;

            ImageDataResoult = new KraImageData(imageWidth, imageHeight, imageName, backgroundSimpleColor, mergedImage);

            // Example of a layer element in the XML:
            /* 
            * <layer locked = "0" collapsed = "0" opacity = "255" uuid = "{06144fe9-488d-4b08-a173-339d985d793a}" y = "0" 
            * channellockflags = "" filename = "layer2" intimeline = "1" channelflags = "" compositeop = "normal" onionskin = "0" 
            * x = "0" visible = "1" selected = "true" colorlabel = "0" name = "Paint Layer 4" colorspacename = "RGBA" nodetype = "paintlayer"/>
            */

            CollectLayersRecursive(image.Element(ns + "layers"), ns, ImageDataResoult.Layers);

            // wrong order or layers first is last, last is first
            foreach (var layer in ImageDataResoult.Layers)
            {
                if (layer.NodeType == KraLayerData.NodeTypeEnum.grouplayer)
                    layer.LayersIdsInGroup.Reverse();
            }
        }

        (List<string> childIds, List<string> masksIds) CollectLayersRecursive(XElement layersElement, XNamespace ns, List<KraLayerData> result, int depth = -1)
        {
            List<string> collectedChildIds = new();
            List<string> collectedMasksIds = new();

            if (layersElement == null)
                return (collectedChildIds, collectedMasksIds);

            depth++;

            foreach (XElement element in layersElement.Elements())
            {
                if (element.Name != ns + "layer" && element.Name != ns + "mask")
                    continue;

                string nodeTypeString = element.Attribute("nodetype")?.Value;

                // if there is unsupported node type, skip it
                if (!Enum.TryParse(typeof(KraLayerData.NodeTypeEnum),
                        nodeTypeString,
                        true,
                        out object nodeTypeObj))
                    continue;

                KraLayerData.NodeTypeEnum nodeType = (KraLayerData.NodeTypeEnum)nodeTypeObj;

                string layerName = element.Attribute("name")?.Value;
                string fileName = element.Attribute("filename")?.Value;
                string uuid = element.Attribute("uuid")?.Value;

                byte opacity = byte.Parse(element.Attribute("opacity")?.Value ?? "255");

                if (nodeType == KraLayerData.NodeTypeEnum.transparencymask)
                { 
                    fileName += ".pixelselection";
                    collectedMasksIds.Add(uuid);
                }

                KraLayerData kraLayerData = new();

                kraLayerData.SetLayerXmlData(
                    fileName,
                    layerName,
                    opacity,
                    uuid,
                    nodeType,
                    depth: depth
                );

                result.Add(kraLayerData);

                if (nodeType == KraLayerData.NodeTypeEnum.paintlayer)
                {
                    CollectMasksForLayer(
                        element,
                        ns,
                        kraLayerData,
                        result,
                        depth
                    );
                }

                if (nodeType == KraLayerData.NodeTypeEnum.grouplayer)
                {
                    XElement childLayersElement = element.Element(ns + "layers");

                    (List<string> childIds, List<string> masksIds) = CollectLayersRecursive(
                        childLayersElement,
                        ns,
                        result,
                        depth
                    );

                    kraLayerData.LayersIdsInGroup.AddRange(childIds);
                    kraLayerData.MasksIds.AddRange(masksIds);
                    collectedChildIds.Add(uuid);

                    continue;
                }

                collectedChildIds.Add(uuid);
            }

            return (collectedChildIds, collectedMasksIds);
        }

        void CollectMasksForLayer(XElement layerElement, XNamespace ns, KraLayerData ownerLayer, List<KraLayerData> result, int depth)
        {
            XElement masksElement = layerElement.Element(ns + "masks");

            if (masksElement == null)
                return;

            depth++;

            foreach (XElement maskElement in masksElement.Elements(ns + "mask"))
            {
                string nodeTypeString = maskElement.Attribute("nodetype")?.Value;

                KraLayerData.NodeTypeEnum nodeType =
                    (KraLayerData.NodeTypeEnum)Enum.Parse(
                        typeof(KraLayerData.NodeTypeEnum),
                        nodeTypeString
                    );

                string layerName = maskElement.Attribute("name")?.Value;
                string fileName = maskElement.Attribute("filename")?.Value + ".pixelselection";
                string uuid = maskElement.Attribute("uuid")?.Value;

                KraLayerData maskLayerData = new();

                maskLayerData.SetLayerXmlData(
                    fileName,
                    layerName,
                    255,
                    uuid,
                    nodeType,
                    depth
                );

                ownerLayer.MasksIds.Add(uuid);
                result.Add(maskLayerData);
            }
        }
       
        void TransferFileToData(ZipArchiveEntry entry)
        {
            var stream = entry.Open();

            var versionLine = ReadLine(stream);
            var versionParts = versionLine.Split(' ');
            var version = int.Parse(versionParts[1]);

            var TileWidthLine = ReadLine(stream);
            var TileWidthParts = TileWidthLine.Split(' ');
            var TileWidth = int.Parse(TileWidthParts[1]);

            var TileHeightLine = ReadLine(stream);
            var TileHeightParts = TileHeightLine.Split(' ');
            var TileHeight = int.Parse(TileHeightParts[1]);

            var pixelSizeLine = ReadLine(stream);
            var pixelSizeParts = pixelSizeLine.Split(' ');
            var pixelSize = int.Parse(pixelSizeParts[1]);

            var tileCountLine = ReadLine(stream);
            var tileCountParts = tileCountLine.Split(' ');
            var tileCount = int.Parse(tileCountParts[1]);

            var kraLayerData = ImageDataResoult.Layers.FirstOrDefault(layer => layer.FileName == entry.Name);
            kraLayerData.SetLayerFileData(version, TileWidth, TileHeight, pixelSize, tileCount);

            kraLayerData.LayerPixels = new Color32[ImageDataResoult.Width * ImageDataResoult.Height];

            for (int i = 0; i < kraLayerData.LayerPixels.Length; i++)
                kraLayerData.LayerPixels[i] = new Color32(0, 0, 0, 0);

            TransferTileDataToLayer(stream, kraLayerData);
        }

        void TransferTileDataToLayer(Stream stream, KraLayerData layerData)
        {
            string line;
            for (int tileIndex = 0; tileIndex < layerData.TileCount; tileIndex++)
            {
                line = ReadLine(stream);

                if (string.IsNullOrWhiteSpace(line))
                {
                    Debug.Log($"Empty tile header at index {tileIndex}");
                    continue;
                }

               // Debug.Log($"Tile header {tileIndex}: {line}");

                var parts = line.Split(',');

                int x = int.Parse(parts[0]);
                int y = int.Parse(parts[1]);
                string type = parts[2];
                int size = int.Parse(parts[3]);

                if (type != "LZF")
                {
                    Debug.Log($"Unsupported tile type: {type}");
                    continue;
                }

                byte[] tilePayload = new byte[size];

                int offset = 0;
                while (offset < size)
                {
                    int read = stream.Read(tilePayload, offset, size - offset);

                    if (read == 0)
                        throw new Exception($"Unexpected end of stream while reading tile {tileIndex}");

                    offset += read;
                }

                byte[] rawTileData = KraDecoder.DecodeKritaTileV2(tilePayload, layerData.PixelSize, layerData.TileWidth, layerData.TileHeight);

                Color32[] tilePixels;

                tilePixels = (layerData.NodeType == KraLayerData.NodeTypeEnum.transparencymask) ? 
                    KraDecoder.RawRgbaToPixelsOneChannel(rawTileData, layerData.TileWidth, layerData.TileHeight, layerData.Opacity) :
                    KraDecoder.RawRgbaToPixels(rawTileData, layerData.TileWidth, layerData.TileHeight, layerData.Opacity);

                KraTextureUtils.PasteTilePixelsIntoLayerPixels(layerData.LayerPixels, ImageDataResoult.Width, ImageDataResoult.Height,
                                                               tilePixels, layerData.TileWidth, layerData.TileHeight, x, y);
            }
        }

        string ReadLine(Stream stream)
        {
            List<byte> bytes = new();

            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1) return null;

                if (b == '\n')
                    break;

                bytes.Add((byte)b);
            }

            return Encoding.ASCII.GetString(bytes.ToArray()).Trim();
        }
    }
}
