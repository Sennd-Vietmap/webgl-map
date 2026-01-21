using System.IO.Compression;
using VectorMap.Core.Geometry;

namespace VectorMap.Core.Tiles;

/// <summary>
/// Parser for Mapbox Vector Tile (MVT) format
/// Uses manual protobuf decoding for the MVT spec
/// </summary>
public class VectorTileParser
{
    private readonly HashSet<string> _layersToLoad;
    
    public VectorTileParser(IEnumerable<string> layersToLoad)
    {
        _layersToLoad = new HashSet<string>(layersToLoad);
    }
    
    /// <summary>
    /// Parse a vector tile from PBF data
    /// </summary>
    public List<FeatureSet> Parse(byte[] data, TileCoordinate tile)
    {
        var result = new List<FeatureSet>();
        
        try
        {
            // Check for gzip compression
            if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
            {
                data = Decompress(data);
            }
            
            var reader = new PbfReader(data);
            
            // Dictionary to collect vertices and indices by layer and type
            var layerDataMap = new Dictionary<string, Dictionary<GeometryType, (List<float> V, List<uint> I)>>();
            
            while (reader.NextField())
            {
                if (reader.Tag == 3) // Layers
                {
                    var bytes = reader.ReadBytes();
                    if (bytes.Length > 0)
                    {
                        ParseLayer(bytes, tile, layerDataMap);
                    }
                }
                else
                {
                    reader.Skip();
                }
            }
            
            // Convert to FeatureSet list
            foreach (var layer in layerDataMap)
            {
                foreach (var typeData in layer.Value)
                {
                    if (typeData.Value.V.Count > 0)
                    {
                        result.Add(new FeatureSet
                        {
                            Coordinate = tile,
                            LayerName = layer.Key,
                            Type = typeData.Key,
                            Vertices = typeData.Value.V.ToArray(),
                            Indices = typeData.Value.I.ToArray()
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error parsing tile {tile}: {ex.Message}");
        }
        
        return result;
    }
    
    private void ParseLayer(byte[] data, TileCoordinate tile, 
        Dictionary<string, Dictionary<GeometryType, (List<float> V, List<uint> I)>> layerData)
    {
        var reader = new PbfReader(data);
        
        string layerName = string.Empty;
        int extent = 4096;
        var keys = new List<string>();
        var values = new List<object>();
        var features = new List<byte[]>();
        
        while (reader.NextField())
        {
            switch (reader.Tag)
            {
                case 1: // name
                    layerName = reader.ReadString();
                    break;
                case 2: // features
                    features.Add(reader.ReadBytes());
                    break;
                case 3: // keys
                    keys.Add(reader.ReadString());
                    break;
                case 4: // values
                    values.Add(ParseValue(reader.ReadBytes()));
                    break;
                case 5: // extent
                    extent = (int)reader.ReadVarint();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        
        if (!_layersToLoad.Contains(layerName))
            return;
            
        if (!layerData.ContainsKey(layerName))
        {
            layerData[layerName] = new Dictionary<GeometryType, (List<float> V, List<uint> I)>
            {
                { GeometryType.Point, (new List<float>(), new List<uint>()) },
                { GeometryType.Line, (new List<float>(), new List<uint>()) },
                { GeometryType.Polygon, (new List<float>(), new List<uint>()) }
            };
        }
        
        foreach (var featureData in features)
        {
            ParseFeature(featureData, tile, extent, layerData[layerName]);
        }
    }
    
    private void ParseFeature(byte[] data, TileCoordinate tile, int extent,
        Dictionary<GeometryType, (List<float> V, List<uint> I)> layerStorage)
    {
        var reader = new PbfReader(data);
        GeometryType type = GeometryType.Unknown;
        byte[]? geometryData = null;
        
        while (reader.NextField())
        {
            switch (reader.Tag)
            {
                case 3: // type
                    type = (GeometryType)reader.ReadVarint();
                    break;
                case 4: // geometry
                    geometryData = reader.ReadPackedVarint();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        
        if (geometryData == null || type == GeometryType.Unknown)
            return;
            
        var coords = DecodeGeometry(geometryData, type, tile, extent);
        var (vList, iList) = layerStorage[type];

        switch (type)
        {
            case GeometryType.Polygon:
                if (coords.Count > 0)
                {
                    var (polyV, polyI) = GeometryConverter.PolygonToVertices(coords.ToArray());
                    uint baseIdx = (uint)(vList.Count / 2);
                    vList.AddRange(polyV);
                    foreach (var idx in polyI) iList.Add(idx + baseIdx);
                }
                break;
                
            case GeometryType.Line:
                foreach (var ring in coords)
                {
                    if (ring.Length >= 2)
                    {
                        var (lineV, lineI) = GeometryConverter.LineToVertices(ring);
                        uint baseIdx = (uint)(vList.Count / 2);
                        vList.AddRange(lineV);
                        foreach (var idx in lineI) iList.Add(idx + baseIdx);
                    }
                }
                break;
                
            case GeometryType.Point:
                foreach (var ring in coords)
                {
                    foreach (var pt in ring)
                    {
                        var pointV = GeometryConverter.PointToVertices(pt);
                        vList.AddRange(pointV);
                    }
                }
                break;
        }
    }
    
    private List<double[][]> DecodeGeometry(byte[] commands, GeometryType type, TileCoordinate tile, int extent)
    {
        var rings = new List<double[][]>();
        var currentRing = new List<double[]>();
        
        int i = 0;
        int cursorX = 0;
        int cursorY = 0;
        
        // Decode packed varints
        var values = new List<uint>();
        var cmdReader = new PbfReader(commands);
        while (cmdReader.Position < commands.Length)
        {
            values.Add((uint)cmdReader.ReadRawVarint());
        }
        
        while (i < values.Count)
        {
            uint cmdInt = values[i++];
            int cmd = (int)(cmdInt & 0x7);
            int count = (int)(cmdInt >> 3);
            
            for (int j = 0; j < count && i < values.Count; j++)
            {
                if (cmd == 1 || cmd == 2) // MoveTo or LineTo
                {
                    if (i + 2 > values.Count) break; // Need 2 values (dx, dy)
                    
                    int dx = ZigZagDecode(values[i++]);
                    int dy = ZigZagDecode(values[i++]);
                    cursorX += dx;
                    cursorY += dy;
                    
                    // Convert tile coordinates to lat/lng
                    var (lng, lat) = TileToLngLat(cursorX, cursorY, tile, extent);
                    
                    if (cmd == 1) // MoveTo - start new ring
                    {
                        if (currentRing.Count > 0)
                        {
                            rings.Add(currentRing.ToArray());
                            currentRing = new List<double[]>();
                        }
                    }
                    
                    currentRing.Add(new[] { lng, lat });
                }
                else if (cmd == 7) // ClosePath
                {
                    if (currentRing.Count > 0)
                    {
                        // Close the polygon by adding the first point again
                        currentRing.Add(currentRing[0]);
                    }
                }
            }
        }
        
        if (currentRing.Count > 0)
        {
            rings.Add(currentRing.ToArray());
        }
        
        return rings;
    }
    
    private static (double lng, double lat) TileToLngLat(int x, int y, TileCoordinate tile, int extent)
    {
        int n = 1 << tile.Z;
        double lng = (tile.X + (double)x / extent) / n * 360.0 - 180.0;
        double latRad = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (tile.Y + (double)y / extent) / n)));
        double lat = latRad * 180.0 / Math.PI;
        return (lng, lat);
    }
    
    private static int ZigZagDecode(uint n)
    {
        return (int)((n >> 1) ^ -(int)(n & 1));
    }
    
    private static object ParseValue(byte[] data)
    {
        var reader = new PbfReader(data);
        while (reader.NextField())
        {
            switch (reader.Tag)
            {
                case 1: return reader.ReadString();
                case 2: return reader.ReadFloat();
                case 3: return reader.ReadDouble();
                case 4: return reader.ReadVarint();
                case 5: return reader.ReadVarint();
                case 6: return reader.ReadVarint();
                case 7: return reader.ReadVarint() != 0;
                default: reader.Skip(); break;
            }
        }
        return string.Empty;
    }
    
    private static byte[] Decompress(byte[] gzipData)
    {
        using var compressedStream = new MemoryStream(gzipData);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();
        gzipStream.CopyTo(resultStream);
        return resultStream.ToArray();
    }
}

/// <summary>
/// Simple protobuf reader for MVT parsing
/// </summary>
internal class PbfReader
{
    private readonly byte[] _data;
    private int _pos;
    private readonly int _end;
    
    public int Position => _pos;
    public int Length => _end;
    public int Remaining => _end - _pos;
    public int Tag { get; private set; }
    public int Type { get; private set; }
    
    public PbfReader(byte[] data)
    {
        _data = data;
        _pos = 0;
        _end = data.Length;
    }
    
    public bool NextField()
    {
        if (_pos >= _end) return false;
        
        ulong val = ReadRawVarint();
        Tag = (int)(val >> 3);
        Type = (int)(val & 0x7);
        return Tag != 0; // Tag 0 is invalid
    }
    
    public void Skip()
    {
        switch (Type)
        {
            case 0: // Varint
                ReadVarint(); 
                break;
            case 1: // 64-bit
                _pos = Math.Min(_pos + 8, _end); 
                break;
            case 2: // Length-delimited
                int len = (int)ReadVarint();
                _pos = Math.Min(_pos + len, _end); 
                break;
            case 5: // 32-bit
                _pos = Math.Min(_pos + 4, _end); 
                break;
        }
    }
    
    public ulong ReadVarint()
    {
        return ReadRawVarint();
    }
    
    public ulong ReadRawVarint()
    {
        ulong result = 0;
        int shift = 0;
        while (_pos < _end && shift < 64)
        {
            byte b = _data[_pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }
    
    public string ReadString()
    {
        int len = (int)ReadVarint();
        if (len <= 0 || _pos + len > _end)
        {
            _pos = Math.Min(_pos + Math.Max(0, len), _end);
            return string.Empty;
        }
        var str = System.Text.Encoding.UTF8.GetString(_data, _pos, len);
        _pos += len;
        return str;
    }
    
    public byte[] ReadBytes()
    {
        int len = (int)ReadVarint();
        if (len <= 0 || _pos + len > _end)
        {
            // Handle invalid length - skip what we can
            int available = Math.Max(0, Math.Min(len, _end - _pos));
            if (available > 0)
            {
                var partial = new byte[available];
                Array.Copy(_data, _pos, partial, 0, available);
                _pos += available;
                return partial;
            }
            return Array.Empty<byte>();
        }
        var bytes = new byte[len];
        Array.Copy(_data, _pos, bytes, 0, len);
        _pos += len;
        return bytes;
    }
    
    public byte[] ReadPackedVarint()
    {
        return ReadBytes();
    }
    
    public float ReadFloat()
    {
        if (_pos + 4 > _end) 
        {
            _pos = _end;
            return 0f;
        }
        float val = BitConverter.ToSingle(_data, _pos);
        _pos += 4;
        return val;
    }
    
    public double ReadDouble()
    {
        if (_pos + 8 > _end)
        {
            _pos = _end;
            return 0.0;
        }
        double val = BitConverter.ToDouble(_data, _pos);
        _pos += 8;
        return val;
    }
}
