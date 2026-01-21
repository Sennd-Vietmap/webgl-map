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
            
            // Dictionary to collect vertices by layer and type
            var layerVertices = new Dictionary<string, Dictionary<GeometryType, List<float>>>();
            
            while (reader.NextField())
            {
                if (reader.Tag == 3) // Layers
                {
                    var layerData = reader.ReadBytes();
                    if (layerData.Length > 0)
                    {
                        ParseLayer(layerData, tile, layerVertices);
                    }
                }
                else
                {
                    reader.Skip();
                }
            }
            
            // Convert to FeatureSet list
            foreach (var layer in layerVertices)
            {
                foreach (var typeVertices in layer.Value)
                {
                    if (typeVertices.Value.Count > 0)
                    {
                        result.Add(new FeatureSet
                        {
                            Coordinate = tile, // Track parent tile
                            LayerName = layer.Key,
                            Type = typeVertices.Key,
                            Vertices = typeVertices.Value.ToArray()
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
        Dictionary<string, Dictionary<GeometryType, List<float>>> layerVertices)
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
        
        // Skip if not a layer we want
        if (!_layersToLoad.Contains(layerName))
            return;
            
        // Initialize layer storage
        if (!layerVertices.ContainsKey(layerName))
        {
            layerVertices[layerName] = new Dictionary<GeometryType, List<float>>
            {
                { GeometryType.Point, new List<float>() },
                { GeometryType.Line, new List<float>() },
                { GeometryType.Polygon, new List<float>() }
            };
        }
        
        // Parse features
        foreach (var featureData in features)
        {
            ParseFeature(featureData, tile, extent, layerVertices[layerName]);
        }
    }
    
    private void ParseFeature(byte[] data, TileCoordinate tile, int extent,
        Dictionary<GeometryType, List<float>> vertices)
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
            
        // Decode geometry commands
        var coords = DecodeGeometry(geometryData, type, tile, extent);
        
        // Convert to vertices based on type
        switch (type)
        {
            case GeometryType.Polygon:
                if (coords.Count > 0)
                {
                    var polygonVertices = GeometryConverter.PolygonToVertices(coords.ToArray());
                    vertices[type].AddRange(polygonVertices);
                }
                break;
                
            case GeometryType.Line:
                foreach (var ring in coords)
                {
                    if (ring.Length >= 2)
                    {
                        var lineVertices = GeometryConverter.LineToVertices(ring);
                        vertices[type].AddRange(lineVertices);
                    }
                }
                break;
                
            case GeometryType.Point:
                foreach (var ring in coords)
                {
                    foreach (var pt in ring)
                    {
                        var pointVertices = GeometryConverter.PointToVertices(pt);
                        vertices[type].AddRange(pointVertices);
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
                    
                    // Convert raw tile integer coordinates to normalized 0..1 range
                    var (nx, ny) = TileToNormalized(cursorX, cursorY, extent);
                    
                    if (cmd == 1) // MoveTo - start new ring
                    {
                        if (currentRing.Count > 0)
                        {
                            rings.Add(currentRing.ToArray());
                            currentRing = new List<double[]>();
                        }
                    }
                    
                    currentRing.Add(new[] { nx, ny });
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
    
    private static (double nx, double ny) TileToNormalized(int x, int y, int extent)
    {
        // Simply normalize the coordinate relative to the tile extent (usually 4096)
        // This is extremely precise as it uses the raw integer offsets
        return ((double)x / extent, (double)y / extent);
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
