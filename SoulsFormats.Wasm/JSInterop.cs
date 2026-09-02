using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using SoulsFormats;

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public partial class JSInterop
{
    [JSExport]
    public static string ReadSoulsFile(string formatName, byte[] data)
    {
        try
        {
            if (formatName == "TPF")
            {
                var tpf = TPF.Read(data);
                var list = new List<object>();
                for (int i = 0; i < tpf.Textures.Count; i++)
                {
                    var tex = tpf.Textures[i];
                    byte[] texBytes = null;
                    string ext = ".dds";
                    try
                    {
                        texBytes = tex.HeaderizeExt(out ext);
                    }
                    catch
                    {
                        texBytes = tex.Bytes;
                    }

                    list.Add(new {
                        ID = i,
                        Name = tex.Name ?? "",
                        Extension = ext ?? ".dds",
                        Format = tex.Format,
                        Type = (int)tex.Type,
                        Mipmaps = tex.Mipmaps,
                        Flags1 = tex.Flags1,
                        Platform = (int)tex.Platform,
                        Bytes = texBytes != null ? Convert.ToBase64String(texBytes) : "",
                        Header = tex.Header,
                        FloatStruct = tex.FloatStruct
                    });
                }

                var dto = new {
                    Platform = (int)tpf.Platform,
                    Encoding = (int)tpf.Encoding,
                    Flag2 = (int)tpf.Flag2,
                    Compression = tpf.Compression,
                    Files = list
                };

                var opts = new JsonSerializerOptions {
                    IncludeFields = true,
                    WriteIndented = false
                };
                opts.Converters.Add(new CompressionInfoConverter());
                return JsonSerializer.Serialize(dto, opts);
            }

            Type type = Type.GetType($"SoulsFormats.{formatName}, SoulsFormats") 
                     ?? Type.GetType($"SoulsFormats.{formatName}");
            if (type == null)
            {
                return JsonSerializer.Serialize(new { error = $"Format '{formatName}' not found." });
            }

            MethodInfo readMethod = type.GetMethod("Read", 
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                null,
                new[] { typeof(byte[]) },
                null);
            if (readMethod == null)
            {
                return JsonSerializer.Serialize(new { error = $"Format '{formatName}' does not have a Read(byte[]) method." });
            }

            object result = readMethod.Invoke(null, new object[] { data });
            
            var options = new JsonSerializerOptions { 
                IncludeFields = true, 
                WriteIndented = false,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles 
            };
            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return JsonSerializer.Serialize(new { error = msg, stackTrace = ex.StackTrace });
        }
    }

    [JSExport]
    public static byte[] WriteSoulsFile(string formatName, string json)
    {
        try
        {
            if (formatName == "TPF")
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tpf = new TPF();
                if (root.TryGetProperty("Platform", out var platProp))
                    tpf.Platform = (TPF.TPFPlatform)platProp.GetInt32();
                if (root.TryGetProperty("Encoding", out var encProp))
                    tpf.Encoding = encProp.GetByte();
                if (root.TryGetProperty("Flag2", out var flag2Prop))
                    tpf.Flag2 = flag2Prop.GetByte();

                if (root.TryGetProperty("Compression", out var compProp))
                {
                    var opts = new JsonSerializerOptions { IncludeFields = true };
                    opts.Converters.Add(new CompressionInfoConverter());
                    tpf.Compression = JsonSerializer.Deserialize<DCX.CompressionInfo>(compProp.GetRawText(), opts) 
                                   ?? new DCX.NoCompressionInfo();
                }

                JsonElement filesElement = default;
                if (root.TryGetProperty("Files", out var fElem))
                    filesElement = fElem;
                else if (root.TryGetProperty("Textures", out var tElem))
                    filesElement = tElem;
                else if (root.ValueKind == JsonValueKind.Array)
                    filesElement = root;

                if (filesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in filesElement.EnumerateArray())
                    {
                        string name = "";
                        if (elem.TryGetProperty("Name", out var nameProp))
                            name = nameProp.GetString() ?? "";

                        byte format = 0;
                        if (elem.TryGetProperty("Format", out var fmtProp))
                            format = fmtProp.GetByte();

                        byte flags1 = 0;
                        if (elem.TryGetProperty("Flags1", out var flags1Prop))
                            flags1 = flags1Prop.GetByte();

                        TPF.TPFPlatform platform = tpf.Platform;
                        if (elem.TryGetProperty("Platform", out var pProp))
                            platform = (TPF.TPFPlatform)pProp.GetInt32();

                        byte[] bytes = null;
                        if (elem.TryGetProperty("Bytes", out var bytesProp))
                        {
                            bytes = bytesProp.GetBytesFromBase64();
                        }

                        // Try to convert standard PC DDS bytes to target platform if applicable
                        if (bytes != null && bytes.Length >= 4 && bytes[0] == (byte)'D' && bytes[1] == (byte)'D' && bytes[2] == (byte)'S' && bytes[3] == (byte)' ')
                        {
                            try
                            {
                                var convertedTex = new TPF.Texture(name, format, flags1, bytes, platform);
                                tpf.Textures.Add(convertedTex);
                                continue;
                            }
                            catch
                            {
                            }
                        }

                        // Fallback / raw texture payload
                        var tex = new TPF.Texture();
                        tex.Name = name;
                        tex.Format = format;
                        tex.Flags1 = flags1;
                        tex.Platform = platform;
                        tex.Bytes = bytes ?? new byte[0];

                        if (elem.TryGetProperty("Type", out var typeProp))
                            tex.Type = (TPF.TexType)typeProp.GetByte();
                        if (elem.TryGetProperty("Mipmaps", out var mipProp))
                            tex.Mipmaps = mipProp.GetByte();

                        tpf.Textures.Add(tex);
                    }
                }

                return tpf.Write();
            }

            Type type = Type.GetType($"SoulsFormats.{formatName}, SoulsFormats") 
                     ?? Type.GetType($"SoulsFormats.{formatName}");
            if (type == null)
            {
                throw new Exception($"Format '{formatName}' not found.");
            }

            var options = new JsonSerializerOptions { 
                IncludeFields = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles 
            };
            options.Converters.Add(new CompressionInfoConverter());
            object instance = JsonSerializer.Deserialize(json, type, options);
            if (instance == null)
            {
                throw new Exception($"Failed to deserialize '{formatName}'.");
            }

            MethodInfo writeMethod = type.GetMethod("Write", Type.EmptyTypes);
            if (writeMethod == null)
            {
                throw new Exception($"Format '{formatName}' does not have a Write() method.");
            }

            return (byte[])writeMethod.Invoke(instance, null);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error writing {formatName}: {ex.Message}", ex);
        }
    }

    public class DcxData
    {
        public SoulsFormats.DCX.CompressionInfo CompressionInfo;
        public byte[] Bytes;
    }

    [JSExport]
    public static string DecompressDCX(byte[] data)
    {
        try
        {
            byte[] uncompressed = SoulsFormats.DCX.Decompress(data, out SoulsFormats.DCX.CompressionInfo compInfo);
            var result = new DcxData { CompressionInfo = compInfo, Bytes = uncompressed };
            var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = false };
            options.Converters.Add(new CompressionInfoConverter());
            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [JSExport]
    public static byte[] CompressDCX(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            options.Converters.Add(new CompressionInfoConverter());
            var parsed = JsonSerializer.Deserialize<DcxData>(json, options);
            if (parsed == null || parsed.Bytes == null || parsed.CompressionInfo == null)
                throw new Exception("Invalid DCX JSON data.");
            return SoulsFormats.DCX.Compress(parsed.Bytes, parsed.CompressionInfo);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error compressing DCX: {ex.Message}", ex);
        }
    }

    public class CompressionInfoConverter : System.Text.Json.Serialization.JsonConverter<SoulsFormats.DCX.CompressionInfo>
    {
        public override SoulsFormats.DCX.CompressionInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("Type", out var typeProp))
                    return new SoulsFormats.DCX.UnkCompressionInfo();
                
                int typeVal = typeProp.GetInt32();
                SoulsFormats.DCX.Type type = (SoulsFormats.DCX.Type)typeVal;

                switch (type)
                {
                    case SoulsFormats.DCX.Type.Unknown: return JsonSerializer.Deserialize<SoulsFormats.DCX.UnkCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.None: return JsonSerializer.Deserialize<SoulsFormats.DCX.NoCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCP_DFLT: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcpDfltCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCP_EDGE: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcpEdgeCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.Zlib: return JsonSerializer.Deserialize<SoulsFormats.DCX.ZlibCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCX_EDGE: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcxEdgeCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCX_DFLT: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcxDfltCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCX_KRAK: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcxKrakCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCX_ZSTD: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcxZstdCompressionInfo>(root.GetRawText(), options);
                    default: return new SoulsFormats.DCX.UnkCompressionInfo();
                }
            }
        }

        public override void Write(Utf8JsonWriter writer, SoulsFormats.DCX.CompressionInfo value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }

    public class ParamDefFieldDTO
    {
        public string DisplayName { get; set; }
        public string InternalName { get; set; }
        public string DisplayType { get; set; }
        public string InternalType { get; set; }
        public string Description { get; set; }
        public string DisplayFormat { get; set; }
        public object Default { get; set; }
        public object Minimum { get; set; }
        public object Maximum { get; set; }
        public object Increment { get; set; }
        public int SortID { get; set; }
        public int ArrayLength { get; set; }
        public int BitSize { get; set; }
    }

    public class ParamRowDTO
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public Dictionary<string, object> Cells { get; set; }
    }

    public class ParamFileDTO
    {
        public string ParamType { get; set; }
        public short ParamdefDataVersion { get; set; }
        public bool BigEndian { get; set; }
        public byte Format2D { get; set; }
        public byte Format2E { get; set; }
        public byte ParamdefFormatVersion { get; set; }
        public short Unk06 { get; set; }
        public bool UnnamedRows { get; set; }
        public bool HeaderlessRows { get; set; }
        public List<ParamDefFieldDTO> Fields { get; set; }
        public List<ParamRowDTO> Rows { get; set; }
    }

    [JSExport]
    public static string GetParamInfo(byte[] data)
    {
        try
        {
            var param = PARAM.Read(data);
            return JsonSerializer.Serialize(new {
                paramType = param.ParamType,
                paramdefDataVersion = param.ParamdefDataVersion,
                bigEndian = param.BigEndian,
                detectedSize = param.DetectedSize,
                rowCount = param.Rows != null ? param.Rows.Count : 0,
                unnamedRows = param.UnnamedRows,
                headerlessRows = param.HeaderlessRows
            });
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return JsonSerializer.Serialize(new { error = msg });
        }
    }

    [JSExport]
    public static string ReadParamWithDef(byte[] data, string paramdefXml)
    {
        try
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(paramdefXml));
            var paramdef = PARAMDEF.XmlDeserialize(ms);

            var param = PARAM.Read(data);
            param.ApplyParamdef(paramdef);

            var fields = new List<ParamDefFieldDTO>();
            foreach (var field in paramdef.Fields)
            {
                fields.Add(new ParamDefFieldDTO {
                    DisplayName = field.DisplayName,
                    InternalName = field.InternalName,
                    DisplayType = field.DisplayType.ToString(),
                    InternalType = field.InternalType,
                    Description = field.Description,
                    DisplayFormat = field.DisplayFormat,
                    Default = field.Default,
                    Minimum = field.Minimum,
                    Maximum = field.Maximum,
                    Increment = field.Increment,
                    SortID = field.SortID,
                    ArrayLength = field.ArrayLength,
                    BitSize = field.BitSize
                });
            }

            var rows = new List<ParamRowDTO>();
            foreach (var row in param.Rows)
            {
                var cellDict = new Dictionary<string, object>();
                if (row.Cells != null)
                {
                    foreach (var cell in row.Cells)
                    {
                        if (cell.Def != null && !string.IsNullOrEmpty(cell.Def.InternalName))
                        {
                            cellDict[cell.Def.InternalName] = cell.Value;
                        }
                    }
                }
                rows.Add(new ParamRowDTO {
                    ID = row.ID,
                    Name = row.Name,
                    Cells = cellDict
                });
            }

            var dto = new ParamFileDTO {
                ParamType = param.ParamType,
                ParamdefDataVersion = param.ParamdefDataVersion,
                BigEndian = param.BigEndian,
                Format2D = (byte)param.Format2D,
                Format2E = (byte)param.Format2E,
                ParamdefFormatVersion = param.ParamdefFormatVersion,
                Unk06 = param.Unk06,
                UnnamedRows = param.UnnamedRows,
                HeaderlessRows = param.HeaderlessRows,
                Fields = fields,
                Rows = rows
            };

            return JsonSerializer.Serialize(dto);
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return JsonSerializer.Serialize(new { error = msg, stackTrace = ex.StackTrace });
        }
    }

    [JSExport]
    public static byte[] WriteParamWithDef(string paramJson, string paramdefXml)
    {
        try
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(paramdefXml));
            var paramdef = PARAMDEF.XmlDeserialize(ms);

            var options = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };
            var dto = JsonSerializer.Deserialize<ParamFileDTO>(paramJson, options);
            if (dto == null)
                throw new Exception("Failed to deserialize ParamFileDTO JSON.");

            var param = new PARAM
            {
                ParamType = dto.ParamType ?? paramdef.ParamType,
                ParamdefDataVersion = dto.ParamdefDataVersion,
                BigEndian = dto.BigEndian,
                Format2D = (PARAM.FormatFlags1)dto.Format2D,
                Format2E = (PARAM.FormatFlags2)dto.Format2E,
                ParamdefFormatVersion = dto.ParamdefFormatVersion,
                Unk06 = dto.Unk06,
                UnnamedRows = dto.UnnamedRows,
                HeaderlessRows = dto.HeaderlessRows,
                Rows = new List<PARAM.Row>()
            };

            typeof(PARAM).GetProperty("AppliedParamdef", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(param, paramdef);

            foreach (var r in dto.Rows)
            {
                var row = new PARAM.Row(r.ID, r.Name, paramdef);
                if (r.Cells != null)
                {
                    foreach (var cell in row.Cells)
                    {
                        if (cell.Def != null && r.Cells.TryGetValue(cell.Def.InternalName, out var rawVal))
                        {
                            SetCellValue(cell, rawVal);
                        }
                    }
                }
                param.Rows.Add(row);
            }

            return param.Write();
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception($"Error writing PARAM: {msg}", ex);
        }
    }

    private static void SetCellValue(PARAM.Cell cell, object rawVal)
    {
        if (rawVal == null) return;

        if (rawVal is JsonElement elem)
        {
            switch (cell.Def.DisplayType)
            {
                case PARAMDEF.DefType.s8:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetSByte() : sbyte.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.u8:
                    if (cell.Def.ArrayLength > 1 && elem.ValueKind == JsonValueKind.String)
                        cell.Value = Convert.FromBase64String(elem.GetString() ?? "");
                    else
                        cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetByte() : byte.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.s16:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetInt16() : short.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.u16:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetUInt16() : ushort.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.s32:
                case PARAMDEF.DefType.b32:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetInt32() : int.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.u32:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetUInt32() : uint.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.f32:
                case PARAMDEF.DefType.angle32:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetSingle() : float.Parse(elem.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case PARAMDEF.DefType.f64:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetDouble() : double.Parse(elem.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case PARAMDEF.DefType.fixstr:
                case PARAMDEF.DefType.fixstrW:
                    cell.Value = elem.GetString() ?? "";
                    break;
                case PARAMDEF.DefType.dummy8:
                    if (cell.Def.BitSize == -1 && cell.Def.ArrayLength > 1 && elem.ValueKind == JsonValueKind.String)
                        cell.Value = Convert.FromBase64String(elem.GetString() ?? "");
                    else
                        cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetByte() : (byte.TryParse(elem.GetString() ?? "0", out var b) ? b : (byte)0);
                    break;
                default:
                    cell.Value = elem.ToString();
                    break;
            }
        }
        else
        {
            cell.Value = rawVal;
        }
    }

    public class TextureRefDTO
    {
        public string Type { get; set; }
        public string Path { get; set; }
    }

    public class FlverMeshDTO
    {
        public string MaterialName { get; set; }
        public float[] Positions { get; set; }
        public float[] Normals { get; set; }
        public float[] UVs { get; set; }
        public int[] Indices { get; set; }
        public List<TextureRefDTO> Textures { get; set; }
    }

    public class FlverNodeDTO
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public int ParentIndex { get; set; }
        public int FirstChildIndex { get; set; }
        public int NextSiblingIndex { get; set; }
        public int PreviousSiblingIndex { get; set; }
        public float[] Translation { get; set; }
        public float[] Rotation { get; set; }
        public float[] Scale { get; set; }
        public float[] BoundingBoxMin { get; set; }
        public float[] BoundingBoxMax { get; set; }
        public int Flags { get; set; }
    }

    public class FlverDummyDTO
    {
        public int Index { get; set; }
        public int ReferenceID { get; set; }
        public int ParentBoneIndex { get; set; }
        public int AttachBoneIndex { get; set; }
        public float[] Position { get; set; }
        public float[] Forward { get; set; }
        public float[] Upward { get; set; }
        public int[] Color { get; set; }
        public bool Flag1 { get; set; }
        public bool UseUpwardVector { get; set; }
    }

    public class FlverGeometryDTO
    {
        public string HeaderVersion { get; set; }
        public List<FlverMeshDTO> Meshes { get; set; }
        public List<string> Nodes { get; set; }
        public List<FlverNodeDTO> BoneNodes { get; set; }
        public List<FlverDummyDTO> Dummies { get; set; }
    }

    [JSExport]
    public static string ReadFlverGeometry(byte[] data)
    {
        try
        {
            List<FlverMeshDTO> meshList = new List<FlverMeshDTO>();
            List<string> nodeNames = new List<string>();
            List<FlverNodeDTO> boneNodes = new List<FlverNodeDTO>();
            List<FlverDummyDTO> dummyList = new List<FlverDummyDTO>();
            string versionStr = "";

            if (FLVER2.Is(data))
            {
                var flver = FLVER2.Read(data);
                versionStr = flver.Header.Version.ToString("X");

                if (flver.Nodes != null)
                {
                    for (int i = 0; i < flver.Nodes.Count; i++)
                    {
                        var node = flver.Nodes[i];
                        nodeNames.Add(node.Name ?? "");
                        boneNodes.Add(new FlverNodeDTO {
                            Index = i,
                            Name = node.Name ?? "",
                            ParentIndex = node.ParentIndex,
                            FirstChildIndex = node.FirstChildIndex,
                            NextSiblingIndex = node.NextSiblingIndex,
                            PreviousSiblingIndex = node.PreviousSiblingIndex,
                            Translation = new float[] { node.Translation.X, node.Translation.Y, node.Translation.Z },
                            Rotation = new float[] { node.Rotation.X, node.Rotation.Y, node.Rotation.Z },
                            Scale = new float[] { node.Scale.X, node.Scale.Y, node.Scale.Z },
                            BoundingBoxMin = new float[] { node.BoundingBoxMin.X, node.BoundingBoxMin.Y, node.BoundingBoxMin.Z },
                            BoundingBoxMax = new float[] { node.BoundingBoxMax.X, node.BoundingBoxMax.Y, node.BoundingBoxMax.Z },
                            Flags = (int)node.Flags
                        });
                    }
                }

                if (flver.Dummies != null)
                {
                    for (int i = 0; i < flver.Dummies.Count; i++)
                    {
                        var d = flver.Dummies[i];
                        dummyList.Add(new FlverDummyDTO {
                            Index = i,
                            ReferenceID = d.ReferenceID,
                            ParentBoneIndex = d.ParentBoneIndex,
                            AttachBoneIndex = d.AttachBoneIndex,
                            Position = new float[] { d.Position.X, d.Position.Y, d.Position.Z },
                            Forward = new float[] { d.Forward.X, d.Forward.Y, d.Forward.Z },
                            Upward = new float[] { d.Upward.X, d.Upward.Y, d.Upward.Z },
                            Color = new int[] { d.Color.R, d.Color.G, d.Color.B, d.Color.A },
                            Flag1 = d.Flag1,
                            UseUpwardVector = d.UseUpwardVector
                        });
                    }
                }

                for (int mIdx = 0; mIdx < flver.Meshes.Count; mIdx++)
                {
                    var mesh = flver.Meshes[mIdx];
                    var matName = (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < flver.Materials.Count)
                        ? flver.Materials[mesh.MaterialIndex].Name
                        : $"Mesh_{mIdx}";

                    List<TextureRefDTO> texRefs = new List<TextureRefDTO>();
                    if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < flver.Materials.Count)
                    {
                        var mat = flver.Materials[mesh.MaterialIndex];
                        if (mat.Textures != null)
                        {
                            foreach (var t in mat.Textures)
                            {
                                texRefs.Add(new TextureRefDTO {
                                    Type = t.ParamName ?? "",
                                    Path = t.Path ?? ""
                                });
                            }
                        }
                    }

                    List<float> positions = new List<float>(mesh.Vertices.Count * 3);
                    List<float> normals = new List<float>(mesh.Vertices.Count * 3);
                    List<float> uvs = new List<float>(mesh.Vertices.Count * 2);

                    foreach (var v in mesh.Vertices)
                    {
                        positions.Add(v.Position.X);
                        positions.Add(v.Position.Y);
                        positions.Add(v.Position.Z);

                        normals.Add(v.Normal.X);
                        normals.Add(v.Normal.Y);
                        normals.Add(v.Normal.Z);

                        if (v.UVs != null && v.UVs.Count > 0)
                        {
                            uvs.Add(v.UVs[0].X);
                            uvs.Add(v.UVs[0].Y);
                        }
                        else
                        {
                            uvs.Add(0f);
                            uvs.Add(0f);
                        }
                    }

                    List<int> indices = new List<int>();
                    if (mesh.FaceSets != null && mesh.FaceSets.Count > 0)
                    {
                        var fs = mesh.FaceSets.FirstOrDefault(f => (f.Flags & FLVER2.FaceSet.FSFlags.LodLevel1) == 0 && (f.Flags & FLVER2.FaceSet.FSFlags.LodLevel2) == 0 && (f.Flags & FLVER2.FaceSet.FSFlags.MotionBlur) == 0)
                              ?? mesh.FaceSets[0];

                        indices = fs.Triangulate(mesh.Vertices.Count < 0xFFFF);
                    }

                    meshList.Add(new FlverMeshDTO {
                        MaterialName = matName,
                        Positions = positions.ToArray(),
                        Normals = normals.ToArray(),
                        UVs = uvs.ToArray(),
                        Indices = indices.ToArray(),
                        Textures = texRefs
                    });
                }
            }
            else if (FLVER0.Is(data))
            {
                var flver = FLVER0.Read(data);
                versionStr = "FLVER0";

                if (flver.Nodes != null)
                {
                    for (int i = 0; i < flver.Nodes.Count; i++)
                    {
                        var node = flver.Nodes[i];
                        nodeNames.Add(node.Name ?? "");
                        boneNodes.Add(new FlverNodeDTO {
                            Index = i,
                            Name = node.Name ?? "",
                            ParentIndex = node.ParentIndex,
                            FirstChildIndex = node.FirstChildIndex,
                            NextSiblingIndex = node.NextSiblingIndex,
                            PreviousSiblingIndex = node.PreviousSiblingIndex,
                            Translation = new float[] { node.Translation.X, node.Translation.Y, node.Translation.Z },
                            Rotation = new float[] { node.Rotation.X, node.Rotation.Y, node.Rotation.Z },
                            Scale = new float[] { node.Scale.X, node.Scale.Y, node.Scale.Z },
                            BoundingBoxMin = new float[] { node.BoundingBoxMin.X, node.BoundingBoxMin.Y, node.BoundingBoxMin.Z },
                            BoundingBoxMax = new float[] { node.BoundingBoxMax.X, node.BoundingBoxMax.Y, node.BoundingBoxMax.Z },
                            Flags = (int)node.Flags
                        });
                    }
                }

                if (flver.Dummies != null)
                {
                    for (int i = 0; i < flver.Dummies.Count; i++)
                    {
                        var d = flver.Dummies[i];
                        dummyList.Add(new FlverDummyDTO {
                            Index = i,
                            ReferenceID = d.ReferenceID,
                            ParentBoneIndex = d.ParentBoneIndex,
                            AttachBoneIndex = d.AttachBoneIndex,
                            Position = new float[] { d.Position.X, d.Position.Y, d.Position.Z },
                            Forward = new float[] { d.Forward.X, d.Forward.Y, d.Forward.Z },
                            Upward = new float[] { d.Upward.X, d.Upward.Y, d.Upward.Z },
                            Color = new int[] { d.Color.R, d.Color.G, d.Color.B, d.Color.A },
                            Flag1 = d.Flag1,
                            UseUpwardVector = d.UseUpwardVector
                        });
                    }
                }

                for (int mIdx = 0; mIdx < flver.Meshes.Count; mIdx++)
                {
                    var mesh = flver.Meshes[mIdx];
                    var matName = (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < flver.Materials.Count)
                        ? flver.Materials[mesh.MaterialIndex].Name
                        : $"Mesh_{mIdx}";

                    List<TextureRefDTO> texRefs = new List<TextureRefDTO>();
                    if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < flver.Materials.Count)
                    {
                        var mat = flver.Materials[mesh.MaterialIndex];
                        if (mat.Textures != null)
                        {
                            foreach (var t in mat.Textures)
                            {
                                texRefs.Add(new TextureRefDTO {
                                    Type = t.ParamName ?? "",
                                    Path = t.Path ?? ""
                                });
                            }
                        }
                    }

                    List<float> positions = new List<float>(mesh.Vertices.Count * 3);
                    List<float> normals = new List<float>(mesh.Vertices.Count * 3);
                    List<float> uvs = new List<float>(mesh.Vertices.Count * 2);

                    foreach (var v in mesh.Vertices)
                    {
                        positions.Add(v.Position.X);
                        positions.Add(v.Position.Y);
                        positions.Add(v.Position.Z);

                        normals.Add(v.Normal.X);
                        normals.Add(v.Normal.Y);
                        normals.Add(v.Normal.Z);

                        if (v.UVs != null && v.UVs.Count > 0)
                        {
                            uvs.Add(v.UVs[0].X);
                            uvs.Add(v.UVs[0].Y);
                        }
                        else
                        {
                            uvs.Add(0f);
                            uvs.Add(0f);
                        }
                    }

                    List<int> indices = new List<int>();
                    if (mesh.Indices != null)
                    {
                        indices = new List<int>(mesh.Indices);
                    }

                    meshList.Add(new FlverMeshDTO {
                        MaterialName = matName,
                        Positions = positions.ToArray(),
                        Normals = normals.ToArray(),
                        UVs = uvs.ToArray(),
                        Indices = indices.ToArray(),
                        Textures = texRefs
                    });
                }
            }
            else
            {
                return JsonSerializer.Serialize(new { error = "File is not a recognized FLVER0 or FLVER2 binary." });
            }

            var dto = new FlverGeometryDTO {
                HeaderVersion = versionStr,
                Meshes = meshList,
                Nodes = nodeNames,
                BoneNodes = boneNodes,
                Dummies = dummyList
            };

            return JsonSerializer.Serialize(dto);
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return JsonSerializer.Serialize(new { error = msg, stackTrace = ex.StackTrace });
        }
    }

    public class TpfTextureDTO
    {
        public string Name { get; set; }
        public string Format { get; set; }
        public string BytesBase64 { get; set; }
    }

    public class TpfFileDTO
    {
        public string Platform { get; set; }
        public List<TpfTextureDTO> Textures { get; set; }
    }

    [JSExport]
    public static string ReadTpfTextures(byte[] data)
    {
        try
        {
            List<TPF> tpfs = new List<TPF>();
            string platformStr = "PC";

            if (TPF.Is(data))
            {
                var tpf = TPF.Read(data);
                platformStr = tpf.Platform.ToString();
                tpfs.Add(tpf);
            }
            else if (BND4.Is(data))
            {
                var bnd = BND4.Read(data);
                foreach (var file in bnd.Files)
                {
                    byte[] fileBytes = file.Bytes;
                    if (DCX.Is(fileBytes))
                    {
                        fileBytes = DCX.Decompress(fileBytes);
                    }
                    if (TPF.Is(fileBytes))
                    {
                        tpfs.Add(TPF.Read(fileBytes));
                    }
                }
            }
            else if (BND3.Is(data))
            {
                var bnd = BND3.Read(data);
                foreach (var file in bnd.Files)
                {
                    byte[] fileBytes = file.Bytes;
                    if (DCX.Is(fileBytes))
                    {
                        fileBytes = DCX.Decompress(fileBytes);
                    }
                    if (TPF.Is(fileBytes))
                    {
                        tpfs.Add(TPF.Read(fileBytes));
                    }
                }
            }
            else
            {
                return JsonSerializer.Serialize(new { error = "File is not a recognized TPF or BND container." });
            }

            var list = new List<TpfTextureDTO>();
            foreach (var tpf in tpfs)
            {
                if (tpf.Textures != null)
                {
                    foreach (var tex in tpf.Textures)
                    {
                        byte[] texBytes = null;
                        try 
                        {
                            texBytes = tex.Headerize();
                        } 
                        catch 
                        {
                            texBytes = tex.Bytes; // Fallback to raw bytes if headerizer fails
                        }

                        list.Add(new TpfTextureDTO {
                            Name = tex.Name ?? "",
                            Format = tex.Format.ToString(),
                            BytesBase64 = texBytes != null ? Convert.ToBase64String(texBytes) : ""
                        });
                    }
                }
            }

            var dto = new TpfFileDTO {
                Platform = platformStr,
                Textures = list
            };

            return JsonSerializer.Serialize(dto);
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return JsonSerializer.Serialize(new { error = msg, stackTrace = ex.StackTrace });
        }
    }

    public class MeshModificationDTO
    {
        public int Index { get; set; }
        public float[] Transform { get; set; }
    }

    public class DummyModificationDTO
    {
        public int Index { get; set; }
        public float[] Position { get; set; }
        public float[] Forward { get; set; }
        public float[] Upward { get; set; }
    }

    public class BoneNodeModificationDTO
    {
        public int Index { get; set; }
        public float[] Translation { get; set; }
        public float[] Rotation { get; set; }
        public float[] Scale { get; set; }
    }

    public class FlverModificationsDTO
    {
        public List<MeshModificationDTO> Meshes { get; set; }
        public List<DummyModificationDTO> Dummies { get; set; }
        public List<BoneNodeModificationDTO> BoneNodes { get; set; }
    }

    [JSExport]
    public static byte[] ModifyAndWriteFlver(byte[] originalData, string modificationsJson)
    {
        try
        {
            byte[] flverBytes = originalData;
            DCX.CompressionInfo compInfo = null;

            if (DCX.Is(originalData))
            {
                flverBytes = DCX.Decompress(originalData, out compInfo);
            }

            var options = new JsonSerializerOptions { IncludeFields = true };
            var mods = JsonSerializer.Deserialize<FlverModificationsDTO>(modificationsJson, options);

            if (FLVER2.Is(flverBytes))
            {
                var flver = FLVER2.Read(flverBytes);

                if (mods != null)
                {
                    if (mods.Meshes != null)
                    {
                        foreach (var mMod in mods.Meshes)
                        {
                            if (mMod.Index >= 0 && mMod.Index < flver.Meshes.Count && mMod.Transform != null && mMod.Transform.Length == 16)
                            {
                                var e = mMod.Transform;
                                Matrix4x4 mat = new Matrix4x4(
                                    e[0], e[1], e[2], e[3],
                                    e[4], e[5], e[6], e[7],
                                    e[8], e[9], e[10], e[11],
                                    e[12], e[13], e[14], e[15]
                                );

                                var mesh = flver.Meshes[mMod.Index];
                                foreach (var v in mesh.Vertices)
                                {
                                    v.Position = Vector3.Transform(v.Position, mat);
                                    if (v.Normal.LengthSquared() > 0.0001f)
                                    {
                                        v.Normal = Vector3.Normalize(Vector3.TransformNormal(v.Normal, mat));
                                    }
                                }
                            }
                        }
                    }

                    if (mods.Dummies != null && flver.Dummies != null)
                    {
                        foreach (var dMod in mods.Dummies)
                        {
                            if (dMod.Index >= 0 && dMod.Index < flver.Dummies.Count)
                            {
                                var d = flver.Dummies[dMod.Index];
                                if (dMod.Position != null && dMod.Position.Length == 3)
                                    d.Position = new Vector3(dMod.Position[0], dMod.Position[1], dMod.Position[2]);
                                if (dMod.Forward != null && dMod.Forward.Length == 3)
                                    d.Forward = new Vector3(dMod.Forward[0], dMod.Forward[1], dMod.Forward[2]);
                                if (dMod.Upward != null && dMod.Upward.Length == 3)
                                    d.Upward = new Vector3(dMod.Upward[0], dMod.Upward[1], dMod.Upward[2]);
                            }
                        }
                    }

                    if (mods.BoneNodes != null && flver.Nodes != null)
                    {
                        foreach (var bMod in mods.BoneNodes)
                        {
                            if (bMod.Index >= 0 && bMod.Index < flver.Nodes.Count)
                            {
                                var node = flver.Nodes[bMod.Index];
                                if (bMod.Translation != null && bMod.Translation.Length == 3)
                                    node.Translation = new Vector3(bMod.Translation[0], bMod.Translation[1], bMod.Translation[2]);
                                if (bMod.Rotation != null && bMod.Rotation.Length == 3)
                                    node.Rotation = new Vector3(bMod.Rotation[0], bMod.Rotation[1], bMod.Rotation[2]);
                                if (bMod.Scale != null && bMod.Scale.Length == 3)
                                    node.Scale = new Vector3(bMod.Scale[0], bMod.Scale[1], bMod.Scale[2]);
                            }
                        }
                    }

                    Vector3 min = new Vector3(float.MaxValue);
                    Vector3 max = new Vector3(float.MinValue);
                    bool hasVerts = false;
                    foreach (var mesh in flver.Meshes)
                    {
                        foreach (var v in mesh.Vertices)
                        {
                            min = Vector3.Min(min, v.Position);
                            max = Vector3.Max(max, v.Position);
                            hasVerts = true;
                        }
                    }
                    if (hasVerts)
                    {
                        flver.Header.BoundingBoxMin = min;
                        flver.Header.BoundingBoxMax = max;
                    }
                }

                byte[] result = flver.Write();
                if (compInfo != null && !(compInfo is DCX.NoCompressionInfo))
                {
                    result = DCX.Compress(result, compInfo);
                }
                return result;
            }
            else if (FLVER0.Is(flverBytes))
            {
                var flver = FLVER0.Read(flverBytes);

                if (mods != null)
                {
                    if (mods.Meshes != null)
                    {
                        foreach (var mMod in mods.Meshes)
                        {
                            if (mMod.Index >= 0 && mMod.Index < flver.Meshes.Count && mMod.Transform != null && mMod.Transform.Length == 16)
                            {
                                var e = mMod.Transform;
                                Matrix4x4 mat = new Matrix4x4(
                                    e[0], e[1], e[2], e[3],
                                    e[4], e[5], e[6], e[7],
                                    e[8], e[9], e[10], e[11],
                                    e[12], e[13], e[14], e[15]
                                );

                                var mesh = flver.Meshes[mMod.Index];
                                foreach (var v in mesh.Vertices)
                                {
                                    v.Position = Vector3.Transform(v.Position, mat);
                                    if (v.Normal.LengthSquared() > 0.0001f)
                                    {
                                        v.Normal = Vector3.Normalize(Vector3.TransformNormal(v.Normal, mat));
                                    }
                                }
                            }
                        }
                    }

                    if (mods.Dummies != null && flver.Dummies != null)
                    {
                        foreach (var dMod in mods.Dummies)
                        {
                            if (dMod.Index >= 0 && dMod.Index < flver.Dummies.Count)
                            {
                                var d = flver.Dummies[dMod.Index];
                                if (dMod.Position != null && dMod.Position.Length == 3)
                                    d.Position = new Vector3(dMod.Position[0], dMod.Position[1], dMod.Position[2]);
                                if (dMod.Forward != null && dMod.Forward.Length == 3)
                                    d.Forward = new Vector3(dMod.Forward[0], dMod.Forward[1], dMod.Forward[2]);
                                if (dMod.Upward != null && dMod.Upward.Length == 3)
                                    d.Upward = new Vector3(dMod.Upward[0], dMod.Upward[1], dMod.Upward[2]);
                            }
                        }
                    }

                    if (mods.BoneNodes != null && flver.Nodes != null)
                    {
                        foreach (var bMod in mods.BoneNodes)
                        {
                            if (bMod.Index >= 0 && bMod.Index < flver.Nodes.Count)
                            {
                                var node = flver.Nodes[bMod.Index];
                                if (bMod.Translation != null && bMod.Translation.Length == 3)
                                    node.Translation = new Vector3(bMod.Translation[0], bMod.Translation[1], bMod.Translation[2]);
                                if (bMod.Rotation != null && bMod.Rotation.Length == 3)
                                    node.Rotation = new Vector3(bMod.Rotation[0], bMod.Rotation[1], bMod.Rotation[2]);
                                if (bMod.Scale != null && bMod.Scale.Length == 3)
                                    node.Scale = new Vector3(bMod.Scale[0], bMod.Scale[1], bMod.Scale[2]);
                            }
                        }
                    }

                    Vector3 min = new Vector3(float.MaxValue);
                    Vector3 max = new Vector3(float.MinValue);
                    bool hasVerts = false;
                    foreach (var mesh in flver.Meshes)
                    {
                        foreach (var v in mesh.Vertices)
                        {
                            min = Vector3.Min(min, v.Position);
                            max = Vector3.Max(max, v.Position);
                            hasVerts = true;
                        }
                    }
                    if (hasVerts)
                    {
                        flver.Header.BoundingBoxMin = min;
                        flver.Header.BoundingBoxMax = max;
                    }
                }

                byte[] result = flver.Write();
                if (compInfo != null && !(compInfo is DCX.NoCompressionInfo))
                {
                    result = DCX.Compress(result, compInfo);
                }
                return result;
            }
            else
            {
                throw new Exception("File is not a valid FLVER0 or FLVER2 binary.");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error modifying FLVER: {ex.Message}", ex);
        }
    }
}
